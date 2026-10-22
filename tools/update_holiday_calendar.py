"""Refresh offline solar terms and independent test fixtures from HKO public tables.

Run explicitly when updating calendar data; builds and runtime never use the network.
"""
import concurrent.futures
import datetime as dt
import hashlib
import json
from pathlib import Path
import re
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
YEARS = list(range(2020, 2036)) + [1995, 2006, 2009]
MONTHS = {name: i for i, name in enumerate(
    ["正月", "二月", "三月", "四月", "五月", "六月", "七月", "八月", "九月", "十月", "十一月", "十二月"], 1)}
DAYS = {"初五": 5, "初七": 7, "初八": 8, "初九": 9, "十五": 15}
FESTIVALS = {(12, 8): "laba", (1, 15): "lantern", (5, 5): "dragon-boat",
             (7, 7): "qixi", (8, 15): "mid-autumn", (9, 9): "double-ninth"}


def fetch(year):
    url = f"https://www.hko.gov.hk/tc/gts/time/calendar/text/files/T{year}c.txt"
    raw = urllib.request.urlopen(url, timeout=20).read()
    text = raw.decode("utf-8-sig")
    rows = []
    for line in text.splitlines():
        match = re.match(r"(\d+)年(\d+)月(\d+)日\s+(\S+)\s+\S+\s*(.*)", line)
        if match:
            y, m, d, lunar, term = match.groups()
            rows.append((dt.date(int(y), int(m), int(d)), lunar, term.strip()))
    if len(rows) != (366 if year % 4 == 0 and (year % 100 != 0 or year % 400 == 0) else 365):
        raise ValueError(f"Incomplete HKO year: {year}")
    return year, url, hashlib.sha256(raw).hexdigest(), rows


def main():
    solar, fixtures, sources = [], [], []

    def add(date, holiday, expected, source):
        fixtures.append({"date": date.isoformat(), "holiday": holiday, "expected": expected, "source": source})

    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        results = sorted(pool.map(fetch, YEARS))
    for year, url, digest, rows in results:
        sources.append({"year": year, "url": url, "sha256": digest})
        terms = {term: date for date, lunar, term in rows if term in ("清明", "冬至")}
        if year >= 2020:
            solar.append({"year": year, "qingming": terms["清明"].isoformat(), "winter_solstice": terms["冬至"].isoformat()})
            for term, holiday in [("清明", "qingming"), ("冬至", "winter-solstice")]:
                for offset in (-1, 0, 1):
                    add(terms[term] + dt.timedelta(days=offset), holiday, offset == 0, url)
        month, leap = None, False
        for date, lunar, term in rows:
            if lunar.endswith("月"):
                leap = lunar.startswith("閏")
                month = MONTHS[lunar.lstrip("閏")]
                if month == 1 and not leap:
                    for offset in (-2, -1, 0, 13, 14):
                        add(date + dt.timedelta(days=offset), "spring-festival", -1 <= offset <= 13, url)
            holiday = FESTIVALS.get((month, DAYS.get(lunar)))
            if holiday:
                add(date, holiday, not leap, url)
                if not leap:
                    add(date - dt.timedelta(days=1), holiday, False, url)
                    add(date + dt.timedelta(days=1), holiday, False, url)
    outputs = {
        ROOT / "assets/holidays/calendar/solar-terms.json": {"schema_version": 1, "source": "https://www.hko.gov.hk/tc/gts/time/conversion.htm", "years": solar},
        ROOT / "tests/fixtures/holidays/calendar-golden.json": {"schema_version": 1, "sources": sources, "cases": fixtures},
    }
    for path, data in outputs.items():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Saved {len(solar)} solar-term years, {len(fixtures)} independent cases, {len(sources)} sources.")


if __name__ == "__main__":
    main()
