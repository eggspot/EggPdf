import glob
import html
import re
import sys

TIME_IN_MICROSECONDS = {"ns": 1e-3, "μs": 1.0, "us": 1.0, "ms": 1e3, "s": 1e6}
SIZE_IN_KILOBYTES = {"B": 1 / 1024, "KB": 1.0, "MB": 1024.0, "GB": 1024.0 * 1024.0}


def load_reports(results_dir):
    rows = {}
    for path in sorted(glob.glob(f"{results_dir}/**/*-report-github.md", recursive=True)):
        header = None
        with open(path, encoding="utf-8") as report:
            for line in report:
                if not line.startswith("|"):
                    header = None
                    continue
                cells = [cell.strip() for cell in line.strip().strip("|").split("|")]
                if header is None:
                    header = cells
                    continue
                if set(cells[0]) <= set("-: "):
                    continue
                row = dict(zip(header, cells))
                rows[html.unescape(row.get("Method", "")).strip("'")] = row
    return rows


def parse_quantity(text, units):
    match = re.match(r"([\d,.]+)\s*(\S+)", text or "")
    if not match or match.group(2) not in units:
        return None
    return float(match.group(1).replace(",", "")) * units[match.group(2)]


def change(base, head):
    if base is None or head is None or base == 0:
        return "—"
    return f"{(head / base - 1) * 100:+.1f}%"


def main(base_dir, head_dir):
    base = load_reports(base_dir)
    head = load_reports(head_dir)
    if not head:
        return 1

    print("| Benchmark | Base | This PR | Time | Base allocated | PR allocated | Allocated |")
    print("|---|---:|---:|---:|---:|---:|---:|")
    for name, head_row in head.items():
        base_row = base.get(name, {})
        base_mean = base_row.get("Mean", "—")
        head_mean = head_row.get("Mean", "—")
        base_alloc = base_row.get("Allocated", "—")
        head_alloc = head_row.get("Allocated", "—")
        time_change = change(parse_quantity(base_mean, TIME_IN_MICROSECONDS), parse_quantity(head_mean, TIME_IN_MICROSECONDS))
        alloc_change = change(parse_quantity(base_alloc, SIZE_IN_KILOBYTES), parse_quantity(head_alloc, SIZE_IN_KILOBYTES))
        print(f"| {name} | {base_mean} | {head_mean} | {time_change} | {base_alloc} | {head_alloc} | {alloc_change} |")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main(sys.argv[1], sys.argv[2]))
