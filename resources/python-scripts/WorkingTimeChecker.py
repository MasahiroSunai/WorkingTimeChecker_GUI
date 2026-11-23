#!/usr/bin/env python
# -*- coding: utf-8 -*-

import argparse
import csv
import os
from html import escape
from typing import Dict, Optional, Set
from xml.sax.saxutils import quoteattr

import pandas as pd
import win32com.client
from atlassian import Confluence

from utils.config_loader import load_config
from utils.logger_utils import setup_logger
from utils.month_utils import resolve_target_month
from utils.path_utils import expand_path

logger = setup_logger("WorkingTimeChecker")

# 各種辞書型およびセット
dicWorkLoad2WebAttendance: Dict[str, str] = {}
dicWebAttendance2WorkLoad: Dict[str, str] = {}
dicWorkLoad: Dict[str, Dict[str, Dict[int, float]]] = {}
dicWebAttendance: Dict[str, Dict[str, Dict[int, float]]] = {}
dicCheckMember: Dict[str, bool] = {}
dicEmployeeName: Dict[str, str] = {}
setLocalTaskJob: Set[str] = set()

# 出力用データフレーム
dfOutWorkLoad = pd.DataFrame(columns=[
    "社員番号", "社員名", "プロジェクト-タスク名", "日付",
    "工数表工数", "Web勤怠工数", "ワーク対応表"
])
dfOutWebAttendance = pd.DataFrame(columns=[
    "社員番号", "社員名", "ワーク名", "日付",
    "Web勤怠工数", "工数表工数", "ワーク対応表"
])
dfOutLastDay = pd.DataFrame(columns=["社員番号", "社員名", "最終勤務日"])
dfLocalTaskAndNoNote = pd.DataFrame(columns=["社員番号", "社員名", "ワーク名", "日付"])


def normalize_employee_id(value) -> str:
    """社員番号を比較用の文字列に統一する"""

    if pd.isna(value):
        return ""

    text = str(value).strip()

    # Excelから12345.0のように取得された場合
    if text.endswith(".0"):
        text = text[:-2]

    return text


def read_check_member(file_path: str):
    """比較対象メンバーをCSVファイルから読み込む。"""
    try:
        with open(file_path, mode="r", encoding="cp932") as file:
            reader = csv.DictReader(file)
            for row in reader:
                if row["比較対象"] == "TRUE":
                    employee_id = normalize_employee_id(row["社員番号"])
                    if not employee_id:
                        logger.warning("社員番号が空のため、比較対象メンバーから除外します")
                        continue
                    employee_name = row["名前"].strip()

                    dicCheckMember[employee_id] = row["社内工数対象"] == "TRUE"
                    dicEmployeeName[employee_id] = employee_name
        logger.info(f"Successfully read check member from {file_path}")
    except Exception:
        logger.exception(f"比較対象メンバーの読み込みに失敗しました: {file_path}")
        raise


def read_working_map(file_path: str):
    """ワークの対応付けをCSVファイルから読み込む。"""
    try:
        with open(file_path, mode="r", encoding="cp932") as file:
            reader = csv.DictReader(file)
            for row in reader:
                prj_name = row["工数集計 プロジェクト名"]
                task_name = row["工数集計 タスク名"]
                work_name = row["Web勤怠 ワーク名"].replace("社内PJT-", "")

                if prj_name == "社内工数":
                    setLocalTaskJob.add(work_name)

                key = f"{prj_name}-{task_name}" if task_name else prj_name
                dicWorkLoad2WebAttendance[key] = work_name
                dicWebAttendance2WorkLoad[work_name] = key
        logger.info(f"Successfully read working map from {file_path}")
    except Exception:
        logger.exception(f"ワーク対応表の読み込みに失敗しました: {file_path}")
        raise


def read_working_load(file_path: str, sheet_name: str = "工数一覧表"):
    """工数表をExcelファイルから読み込む。"""
    try:
        ws = pd.read_excel(file_path, sheet_name=sheet_name, header=5)
        for row in ws.itertuples(index=False):
            if pd.isna(row[1]) or pd.isna(row[2]):
                continue

            employee_id = normalize_employee_id(row[1])
            if not employee_id:
                logger.warning("社員番号が空の工数表データを除外します")
                continue
            employee_name = str(row[2]).strip()

            dicEmployeeName.setdefault(employee_id, employee_name)
            project_name = str(row[8]).strip()
            task_name = str(row[9]).strip() if pd.notna(row[9]) else None

            if project_name.startswith("休憩"):
                continue

            key = f"{project_name}-{task_name}" if task_name else project_name
            dicWorkLoad.setdefault(employee_id, {}).setdefault(key, {})

            for col in range(12, 43):
                day = col - 11
                if pd.notna(row[col]):
                    worktime = row[col]
                    dicWorkLoad[employee_id][key].setdefault(day, 0)
                    dicWorkLoad[employee_id][key][day] += worktime

        logger.info(f"Successfully read working load from {file_path}")
    except Exception:
        logger.exception(f"工数表の読み込みに失敗しました: {file_path}")
        raise


def read_web_attendance(file_path: str):
    """Web勤怠をCSVファイルから読み込む。"""
    try:
        temp_last_day = {}
        with open(file_path, mode="r", encoding="cp932") as file:
            reader = csv.DictReader(file)
            for row in reader:
                employee_id = normalize_employee_id(row["社員番号"])
                if not employee_id:
                    logger.warning("社員番号が空のWeb勤怠データを除外します")
                    continue

                employee_name = row["社員名称: 社員名"].strip()

                dicEmployeeName.setdefault(employee_id, employee_name)
                work_name = row["ワーク番号: ワーク名"].strip()
                day = int(row["勤務日"].split("/")[2])
                work_time = float(row["時間・分"]) / 60

                temp_last_day[employee_id] = max(
                    temp_last_day.get(employee_id, 0), day
                )

                if work_name.startswith("準）"):
                    continue

                if (
                    work_name in setLocalTaskJob
                    and employee_id in dicCheckMember
                    and dicCheckMember[employee_id]
                ):
                    note = row.get("備考", "")
                    if pd.isna(note) or str(note).strip() == "":
                        dfLocalTaskAndNoNote.loc[len(dfLocalTaskAndNoNote)] = [
                            employee_id,
                            dicEmployeeName.get(employee_id, employee_name),
                            work_name,
                            day,
                        ]

                dicWebAttendance.setdefault(employee_id, {}).setdefault(
                    work_name, {}
                ).setdefault(day, 0)
                dicWebAttendance[employee_id][work_name][day] += work_time

        for employee_id, last_day in sorted(temp_last_day.items()):
            employee_name = dicEmployeeName.get(employee_id, "")
            dfOutLastDay.loc[len(dfOutLastDay)] = [
                employee_id, employee_name, last_day
            ]

        logger.info(f"Successfully read web attendance from {file_path}")
    except Exception:
        logger.exception(f"Web勤怠の読み込みに失敗しました: {file_path}")
        raise


def check_web_attendance_work_day(file_path: str) -> Optional[pd.DataFrame]:
    """勤務日とワーク開始・完了予定日の不整合を確認する。"""
    try:
        df = pd.read_csv(
            file_path,
            encoding="cp932",
            usecols=[
                "社員番号", "社員名称: 社員名", "勤務日", "ワーク番号: ワーク名",
                "ワーク番号: ワーク開始予定日", "ワーク番号: ワーク完了予定日",
            ],
        )
        output = df.query(
            "`ワーク番号: ワーク開始予定日` > `勤務日` or "
            "`ワーク番号: ワーク完了予定日` < `勤務日`"
        ).rename(columns={
            "社員名称: 社員名": "社員名",
            "ワーク番号: ワーク名": "ワーク名",
            "ワーク番号: ワーク開始予定日": "ワーク開始予定日",
            "ワーク番号: ワーク完了予定日": "ワーク完了予定日",
        }).copy()

        # 社員番号を比較用の形式へ統一
        output["社員番号"] = output["社員番号"].apply(normalize_employee_id)

        # 比較対象メンバーに含まれる社員番号だけを残す
        output = output[
            output["社員番号"].isin(dicCheckMember.keys())
        ].copy()

        if output.empty:
            logger.info("勤務日とワーク開始・完了予定日の不整合はありません")
            return None

        return output
    except Exception:
        logger.exception(f"勤務日とワーク期間の確認に失敗しました: {file_path}")
        raise


def check_work_load_to_web_attendance():
    """工数表を基準にWeb勤怠との不整合を確認する。"""
    for employee_id, project_tasks in dicWorkLoad.items():
        if employee_id not in dicCheckMember:
            continue
        employee_name = dicEmployeeName.get(employee_id, "")

        for project_task, days in project_tasks.items():
            if "社内工数" in project_task and not dicCheckMember[employee_id]:
                continue

            for day, workload_time in days.items():
                if workload_time == 0:
                    continue

                web_time = 0
                if employee_id in dicWebAttendance:
                    for work_name, web_days in dicWebAttendance[employee_id].items():
                        if (
                            project_task in dicWorkLoad2WebAttendance
                            and work_name == dicWorkLoad2WebAttendance[project_task]
                            and day in web_days
                        ):
                            web_time += web_days[day]

                if workload_time != web_time:
                    mapping_status = (
                        "登録済み"
                        if project_task in dicWorkLoad2WebAttendance
                        else "未登録"
                    )
                    dfOutWorkLoad.loc[len(dfOutWorkLoad)] = [
                        employee_id,
                        employee_name,
                        project_task,
                        int(day),
                        round(workload_time, 2),
                        round(web_time, 2),
                        mapping_status,
                    ]


def check_web_attendance_to_work_load():
    """Web勤怠を基準に工数表との不整合を確認する。"""
    for employee_id, work_days in dicWebAttendance.items():
        if employee_id not in dicCheckMember:
            continue
        employee_name = dicEmployeeName.get(employee_id, "")

        for work_name, days in work_days.items():
            if work_name in setLocalTaskJob and not dicCheckMember[employee_id]:
                continue

            for day, web_time in days.items():
                if web_time == 0:
                    continue

                workload_time = 0
                if employee_id in dicWorkLoad:
                    for project_task, project_days in dicWorkLoad[employee_id].items():
                        if (
                            work_name in dicWebAttendance2WorkLoad
                            and project_task == dicWebAttendance2WorkLoad[work_name]
                            and day in project_days
                        ):
                            workload_time += project_days[day]

                if web_time != workload_time:
                    mapping_status = (
                        "登録済み"
                        if work_name in dicWebAttendance2WorkLoad
                        else "未登録"
                    )
                    dfOutWebAttendance.loc[len(dfOutWebAttendance)] = [
                        employee_id,
                        employee_name,
                        work_name,
                        int(day),
                        round(web_time, 2),
                        round(workload_time, 2),
                        mapping_status,
                    ]


def update_confluence_page(confluence, page_id, body, title, always_update=False):
    """Confluenceページを更新する。"""
    try:
        page = confluence.get_page_by_id(page_id, expand="body.storage")
        current_body = page["body"]["storage"]["value"]

        if not always_update and current_body == body:
            logger.info(f"Confluence page is already up to date: {page_id}")
            return False

        confluence.update_page(
            page_id=page_id,
            title=title,
            body=body,
            representation="storage",
        )
        logger.info(f"Confluence page updated: {page_id}")
        return True
    except Exception:
        logger.exception(f"ページ更新失敗: {page_id}")
        raise


def send_mail(to_address: str, subject: str, html_body: str) -> None:
    """メールを送信する。"""
    try:
        outlook = win32com.client.Dispatch("Outlook.Application")
        mail = outlook.CreateItem(0)
        mail.To = to_address
        mail.Subject = subject
        mail.BodyFormat = 2
        mail.HTMLBody = html_body
        mail.Send()
        logger.info(f"Email sent to {to_address}")
    except Exception:
        logger.exception(f"メール送信に失敗しました: {to_address}")
        raise


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Web勤怠と工数表の不整合チェックツール"
    )
    parser.add_argument(
        "--config", default="config/config.yaml", help="config.yaml のパス"
    )
    return parser.parse_args()


def format_html_value(value, column_name: str) -> str:
    """表に表示する値をHTML用に整形する。"""
    if pd.isna(value):
        return ""

    if column_name in ("工数表工数", "Web勤怠工数"):
        try:
            return f"{float(value):.2f}"
        except (TypeError, ValueError):
            pass

    return escape(str(value))


def dataframe_to_styled_html(
    dataframe: pd.DataFrame,
    gray_unmapped: bool = False,
    hide_mapping_column: bool = False,
) -> str:
    """DataFrameを統一された見た目のHTML表へ変換する。"""
    display_columns = [
        column
        for column in dataframe.columns
        if not (hide_mapping_column and column == "ワーク対応表")
    ]

    html_parts = [
        '<table style="border-collapse: collapse; width: 100%; '
        'margin-top: 8px; margin-bottom: 24px; font-size: 14px;">',
        "<thead>",
        "<tr>",
    ]

    numeric_columns = {"日付", "勤務日", "最終勤務日", "工数表工数", "Web勤怠工数"}

    for column in display_columns:
        align = "right" if column in numeric_columns else "left"
        html_parts.append(
            '<th style="background-color: #f2f2f2; border: 1px solid #b7b7b7; '
            f'padding: 7px 8px; font-weight: bold; text-align: {align}; '
            f'white-space: nowrap;">{escape(str(column))}</th>'
        )

    html_parts.extend(["</tr>", "</thead>", "<tbody>"])

    if dataframe.empty:
        html_parts.append(
            f'<tr><td colspan="{max(len(display_columns), 1)}" '
            'style="border: 1px solid #d0d0d0; padding: 10px; color: #555555; '
            'background-color: #fafafa;">該当なし</td></tr>'
        )

    previous_employee_id = None

    for row_index, (_, row) in enumerate(dataframe.iterrows()):
        current_employee_id = row.get("社員番号") if "社員番号" in dataframe.columns else None
        is_new_employee = (
            previous_employee_id is not None
            and current_employee_id != previous_employee_id
        )
        is_unmapped = (
            gray_unmapped
            and "ワーク対応表" in dataframe.columns
            and row.get("ワーク対応表") == "未登録"
        )
        normal_background = "#ffffff" if row_index % 2 == 0 else "#fafafa"

        html_parts.append("<tr>")

        for column in display_columns:
            value = row[column]
            styles = [
                "border: 1px solid #d0d0d0",
                "padding: 7px 8px",
                "vertical-align: top",
            ]

            if is_new_employee:
                styles.append("border-top: 3px solid #808080")

            if is_unmapped:
                styles.extend(["background-color: #d9d9d9", "color: #555555"])
            else:
                styles.append(f"background-color: {normal_background}")

            if column in ("工数表工数", "Web勤怠工数"):
                styles.extend(["font-weight: bold", "text-align: right", "white-space: nowrap"])
            elif column in ("日付", "勤務日", "最終勤務日"):
                styles.extend(["text-align: right", "white-space: nowrap"])
            elif column == "社員番号":
                styles.append("white-space: nowrap")
            elif column == "ワーク対応表" and value == "未登録":
                styles.append("font-weight: bold")

            html_parts.append(
                f'<td style="{"; ".join(styles)};">'
                f'{format_html_value(value, column)}</td>'
            )

        html_parts.append("</tr>")
        previous_employee_id = current_employee_id

    html_parts.extend(["</tbody>", "</table>"])
    return "\n".join(html_parts)


def build_section_title(number: int, title: str) -> str:
    """統一されたセクション見出しを作成する。"""
    return (
        '<div style="margin-top: 28px; margin-bottom: 8px; padding: 8px 10px; '
        'background-color: #f2f2f2; border-left: 5px solid #666666; '
        'font-size: 18px; font-weight: bold;">'
        f"{number}. {escape(title)}</div>"
    )


def build_attachment_links(files: list[str]) -> str:
    """Confluence添付ファイルリンクを作成する。"""
    links = []
    for file_path in files:
        if not file_path:
            continue
        filename_attr = quoteattr(os.path.basename(file_path))
        links.append(
            f"""<p>
                <ac:link>
                    <ri:attachment ri:filename={filename_attr}/>
                </ac:link>
            </p>"""
        )
    return "\n".join(links)


def build_html_body(vars, df_workday_mismatch, attachment_links_block) -> str:
    """メールとConfluenceで共通利用するHTML本文を作成する。"""
    workload_output = dfOutWorkLoad.sort_values(
        ["社員番号", "日付", "プロジェクト-タスク名"]
    )
    web_attendance_output = dfOutWebAttendance.sort_values(
        ["社員番号", "日付", "ワーク名"]
    )
    last_day_output = dfOutLastDay.sort_values(["社員番号"])

    check_member_output = pd.DataFrame(
        [
            {
                "社員番号": employee_id,
                "社員名": dicEmployeeName.get(employee_id, ""),
                "社内工数対象": (
                    "対象"
                    if is_local_task_target
                    else "対象外"
                ),
            }
            for employee_id, is_local_task_target
            in dicCheckMember.items()
        ],
        columns=[
            "社員番号",
            "社員名",
            "社内工数対象",
        ],
    )

    check_member_output = check_member_output.sort_values(
        ["社員番号"]
    )

    parts = [
        '<div style="margin-bottom: 16px; padding-bottom: 8px; '
        'border-bottom: 2px solid #666666;">'
        '<span style="font-size: 22px; font-weight: bold;">'
        f"{vars['YYYY']}年{vars['MM']}月 工数表とWeb勤怠の不整合チェック結果"
        "</span></div>",
        '<div style="margin: 12px 0 20px 0; padding: 10px 12px; '
        'background-color: #fff8e1; border: 1px solid #e0c96c;">'
        "※Web勤怠はAppsから取得しているため、前日または前々日までの"
        "入力内容となる場合があります。</div>",
        '<div style="margin: 10px 0 18px 0;">'
        '<span style="display: inline-block; background-color: #d9d9d9; '
        'color: #555555; border: 1px solid #b7b7b7; padding: 3px 10px; '
        'margin-right: 6px;">灰色の行</span>'
        "ワーク対応表に登録されていない項目です。</div>",
    ]

    parts.extend([
        build_section_title(1, "工数表 ⇒ Web勤怠の不整合チェック結果"),
        '<p style="margin: 6px 0 10px 0; color: #555555;">'
        "工数表に入力された工数を基準に、対応するWeb勤怠の工数と比較した結果です。</p>",
        dataframe_to_styled_html(workload_output, gray_unmapped=True),
        build_section_title(2, "Web勤怠 ⇒ 工数表の不整合チェック結果"),
        '<p style="margin: 6px 0 10px 0; color: #555555;">'
        "Web勤怠に入力された工数を基準に、対応する工数表の工数と比較した結果です。</p>",
        dataframe_to_styled_html(web_attendance_output, gray_unmapped=True),
        build_section_title(3, "勤務日とワーク開始・完了予定日の不整合チェック結果"),
    ])

    if df_workday_mismatch is None:
        parts.append(
            '<div style="margin: 8px 0 20px 0; padding: 10px; '
            'background-color: #fafafa; border: 1px solid #d0d0d0; '
            'color: #555555;">不整合なし</div>'
        )
    else:
        parts.append(
            dataframe_to_styled_html(
                df_workday_mismatch.sort_values(["社員番号", "勤務日"])
            )
        )

    parts.extend([
        build_section_title(4, "社内工数で備考未入力の一覧"),
        '<p style="margin: 6px 0 10px 0; color: #555555;">'
        "社内工数チェック対象者のみを表示しています。</p>",
        dataframe_to_styled_html(
            dfLocalTaskAndNoNote.sort_values(["社員番号", "日付"])
        ),
        build_section_title(5, "最終勤務入力日"),
        dataframe_to_styled_html(last_day_output),
        build_section_title(6, "チェック対象メンバー"),
        dataframe_to_styled_html(check_member_output),
        build_section_title(7, "添付ファイル"),
        '<p style="margin: 6px 0 10px 0; color: #555555;">'
        "今回の確認に使用したファイルです。</p>",
        attachment_links_block,
    ])

    return "".join(parts)


def main():
    """メイン処理。"""
    logger.info("WorkingTimeChecker start")

    try:
        args = parse_args()
        config = load_config(args.config)

        vars = resolve_target_month(config)
        paths = config["paths"]
        confluence_conf = config["confluence"]
        dry_run = config.get("system", {}).get("dry_run", False)
        send_mail_enabled = config.get("system", {}).get("send_mail", True)
        logger.info(f"dry_run={dry_run}, send_mail={send_mail_enabled}")

        member_file = expand_path(paths["member_file"], vars)
        workmap_file = expand_path(paths["work_map_file"], vars)
        workload_file = expand_path(paths["workload_aggregate_file"], vars)
        web_attendance_file = expand_path(paths["web_attendance_file"], vars)

        dndev_conf = confluence_conf["dndev"]
        confluence_url = dndev_conf["url"]
        target_page_id = dndev_conf["upload_page_id"]
        cert_dir = dndev_conf["cert_dir"]
        to_mail = config["system"].get("toAddress")

        aws_id = os.environ.get("DNDEV_AWS_ID")
        aws_password = os.environ.get("DNDEV_AWS_PASSWORD")
        if not aws_id or not aws_password:
            raise RuntimeError(
                "DNDEV_AWS_ID / DNDEV_AWS_PASSWORD が環境変数にありません"
            )

        read_check_member(member_file)
        read_working_map(workmap_file)
        read_working_load(workload_file)
        read_web_attendance(web_attendance_file)

        check_work_load_to_web_attendance()
        check_web_attendance_to_work_load()
        workday_mismatch = check_web_attendance_work_day(web_attendance_file)

        files_to_attach = [
            workload_file,
            web_attendance_file,
            workmap_file,
            member_file,
        ]
        attachment_links = build_attachment_links(files_to_attach)
        html_body = build_html_body(vars, workday_mismatch, attachment_links)

        if not send_mail_enabled:
            logger.info(
                "設定によりメール送信をスキップしました: "
                "system.send_mail=false"
            )
        elif not to_mail:
            logger.info(
                "メール送信先が未設定のため、メール送信をスキップしました"
            )
        elif dry_run:
            logger.info(
                f"Dry runのためメール送信をスキップしました: {to_mail}"
            )
        else:
            send_mail(
                to_address=to_mail,
                subject="【自動送信】【PXT連絡】工数表⇔Web勤怠相違",
                html_body=html_body,
            )

        cert_key_path = os.path.join(cert_dir, "client.key")
        cert_file_path = os.path.join(cert_dir, "client.cert")
        if not os.path.exists(cert_key_path) or not os.path.exists(cert_file_path):
            raise FileNotFoundError(
                f"証明書ファイルが見つかりません: "
                f"{cert_key_path} または {cert_file_path}"
            )

        if target_page_id:
            if dry_run:
                logger.info(
                    f"Dry run: Confluence更新と添付をスキップしました: {target_page_id}"
                )
            else:
                confluence = Confluence(
                    url=confluence_url,
                    username=aws_id,
                    password=aws_password,
                    verify_ssl=True,
                    cert=(cert_file_path, cert_key_path),
                )
                logger.info(f"Confluence update start: {target_page_id}")
                updated = update_confluence_page(
                    confluence=confluence,
                    page_id=target_page_id,
                    body=html_body,
                    title=(
                        "工数表とWeb勤怠の不整合チェック結果 - "
                        f"{vars['YYYY']}/{vars['MM']}"
                    ),
                    always_update=True,
                )
                logger.info(
                    f"Confluence update completed. "
                    f"page_id={target_page_id}, updated={updated}"
                )

                for file_path in files_to_attach:
                    if file_path:
                        confluence.attach_file(
                            page_id=target_page_id,
                            filename=file_path,
                            name=os.path.basename(file_path),
                        )
                        logger.info(
                            f"Confluence attachment uploaded: {os.path.basename(file_path)}"
                        )

        logger.info("WorkingTimeChecker finished successfully")
    except Exception as error:
        logger.exception(f"エラーが発生しました: {error}")
        raise


if __name__ == "__main__":
    main()
