# -*- coding: utf-8 -*-
"""Kuveyt Türk SWIFT maili <-> model <-> beklenen satırlar.

Model: {"date": str, "sections": [{"name", "lead": [str], "rows": [row]}]}
row:   ("label", text) | ("kv", label, [değer satırları])   (ikinci ve sonraki değer satırı banka adıdır)
"""
import html as H
import re
from html.parser import HTMLParser

SEP_RE = re.compile(r"^-{3,}\s*(.+?)\s*-{3,}$")
KV_RE = re.compile(r"^(.*?\S)\s{3,}:(?:\s{3,}(.*))?\s*$")

CANON = {
    "Message Header": "---------------------------- Message Header ----------------------------",
    "Message Text": "---------------------------- Message Text ------------------------------",
    "Interventions": "----------------------- Interventions ----------------------------------",
}
COLS = {"Message Header": (36, 3), "Message Text": (39, 3), "Interventions": (31, 0)}


# ---------------- ham metin -> model ----------------
def ham_to_model(ham):
    model = {"date": None, "sections": []}
    sec = None
    for raw in ham.splitlines():
        if not raw.strip():
            continue
        s = raw.strip()
        m = SEP_RE.match(s)
        if m:
            sec = {"name": m.group(1).strip(), "lead": [], "rows": []}
            model["sections"].append(sec)
            continue
        if sec is None:
            model["date"] = s
            continue
        if s.startswith("(") and sec["rows"] and sec["rows"][-1][0] == "kv":
            sec["rows"][-1][2].append(s)
            continue
        km = KV_RE.match(raw.rstrip() + ("" if raw.rstrip().endswith(":") else ""))
        if km and "    :" in raw:
            sec["rows"].append(("kv", km.group(1).strip(), [(km.group(2) or "").strip()]))
            continue
        if sec["name"] == "Message Header" and not sec["rows"] and s.startswith("Swift "):
            sec["lead"].append(s)
            continue
        sec["rows"].append(("label", s))
    return model


# ---------------- model -> beklenen satırlar ----------------
def expected_lines(model):
    out = []
    if model["date"]:
        out.append("n  | " + model["date"])
    for sec in model["sections"]:
        col, ind = COLS.get(sec["name"], (39, 3))
        out.append("b  | " + CANON.get(sec["name"], "---------------------------- " + sec["name"] + " ----------------------------"))
        for t in sec["lead"]:
            out.append("b  | " + t)
        for r in sec["rows"]:
            if r[0] == "label":
                out.append("b  | " + r[1])
            else:
                vals = r[2] or [""]
                out.append("kv | " + (" " * ind + r[1]).ljust(col) + ": " + vals[0])
                for extra in vals[1:]:
                    out.append("n  | " + " " * (col + 2) + extra)
    return out


def model_to_ham(model):
    lines = [model["date"]] if model["date"] else []
    for sec in model["sections"]:
        lines.append(CANON.get(sec["name"], sec["name"]))
        lines += sec["lead"]
        for r in sec["rows"]:
            if r[0] == "label":
                lines.append(r[1])
            else:
                lines.append(f"{r[1]}    :    {r[2][0] if r[2] else ''}")
                lines += r[2][1:]
    return "\n".join(lines) + "\n"


# ---------------- model -> bankanın HTML tablosu ----------------
SP = "<span style='font-size:9.0pt;font-family:\"Courier New\"'>"
PAD0 = "style='padding:0cm 0cm 0cm 0cm'"


def e(t):
    return H.escape(t, quote=False)


def _label_td(text):
    return f"<td {PAD0}><p class=MsoNormal><b>{SP}{e(text)}</span></b>{SP}<o:p></o:p></span></p></td>"


def _rows_html(sec):
    inter = sec["name"] == "Interventions"
    parts = []
    seen_kv = False
    for r in sec["rows"]:
        if r[0] == "label":
            if seen_kv or inter:
                parts.append(f"<tr>{_label_td(r[1])}<td {PAD0}></td><td {PAD0}></td></tr>")
            else:
                parts.append(f"<tr>{_label_td(r[1])}</tr>")
            continue
        seen_kv = True
        label, vals = r[1], (r[2] or [""])
        value_html = "<br>".join(e(v) for v in vals)
        multi = len(vals) > 1
        if inter:
            vt = " valign=top" if multi else ""
            parts.append(
                f"<tr><td{vt} {PAD0}><p class=MsoNormal><b>{SP}{e(label)}</span></b>{SP} <o:p></o:p></span></p></td>"
                f"<td {PAD0}><p class=MsoNormal><b>{SP}:</span></b>{SP} <o:p></o:p></span></p></td>"
                f"<td style='padding:0cm 0cm 0cm 3.75pt'><p class=MsoNormal>{SP}{value_html} <o:p></o:p></span></p></td></tr>")
        elif multi:
            parts.append(
                f"<tr><td valign=top style='padding:0cm 0cm 0cm 11.25pt'><p class=MsoNormal><b>{SP}{e(label)}</span></b>{SP}<o:p></o:p></span></p></td>"
                f"<td valign=top {PAD0}><p class=MsoNormal><b>{SP}:</span></b>{SP}<o:p></o:p></span></p></td>"
                f"<td style='padding:0cm 0cm 0cm 3.75pt'><p class=MsoNormal>{SP}{value_html}<o:p></o:p></span></p></td></tr>")
        else:
            parts.append(
                f"<tr><td style='padding:0cm 0cm 0cm 11.25pt'><p class=MsoNormal><b>{SP}{e(label)}</span></b>{SP}<o:p></o:p></span></p></td>"
                f"<td {PAD0}><p class=MsoNormal>{SP}:<o:p></o:p></span></p></td>"
                f"<td style='padding:0cm 0cm 0cm 3.75pt'><p class=MsoNormal>{SP}{value_html}<o:p></o:p></span></p></td></tr>")
    return "".join(parts)


def model_to_table(model):
    rows = []
    if model["date"]:
        rows.append(f"<tr><td {PAD0}><p class=MsoNormal>{SP}{e(model['date'])} <o:p></o:p></span></p></td></tr>")
    for sec in model["sections"]:
        rule = CANON.get(sec["name"], "---------------------------- " + sec["name"] + " ----------------------------")
        rows.append(f"<tr><td {PAD0}><p><strong>{SP}{e(rule)}</span></strong>{SP} <o:p></o:p></span></p></td></tr>")
        for t in sec["lead"]:
            rows.append(f"<tr><td {PAD0}><p class=MsoNormal><strong>{SP}{e(t)}</span></strong>{SP} <o:p></o:p></span></p></td></tr>")
        if sec["name"] == "Interventions":
            rows.append(f"<tr><td valign=top {PAD0}><table class=MsoNormalTable border=0 cellspacing=0 cellpadding=0 "
                        f"id=TableMessageInterventions><tr><td style='padding:0cm 0cm 0cm 11.25pt'></td></tr>"
                        f"{_rows_html(sec)}</table></td></tr>")
        else:
            rows.append(f"<tr><td {PAD0}><table class=MsoNormalTable border=0 cellspacing=0 cellpadding=0>"
                        f"{_rows_html(sec)}</table></td></tr>")
    return ("<table class=MsoNormalTable border=0 cellspacing=0 cellpadding=0 id=TableMessagePrintView>"
            + "".join(rows) + "</table>")


def table_span(html_text):
    """TableMessagePrintView tablosunun (iç tablolar dahil) başlangıç ve bitiş konumu."""
    i = html_text.find("id=TableMessagePrintView")
    if i < 0:
        i = html_text.find('id="TableMessagePrintView"')
    start = html_text.rfind("<table", 0, i)
    depth, pos = 0, start
    tag = re.compile(r"<(/?)table\b", re.I)
    for m in tag.finditer(html_text, start):
        depth += -1 if m.group(1) else 1
        if depth == 0:
            end = html_text.find(">", m.end()) + 1
            return start, end
    raise ValueError("tablo kapanmadı")


def replace_table(template_html, model):
    s, t = table_span(template_html)
    return template_html[:s] + model_to_table(model) + template_html[t:]


# ---------------- gerçek HTML -> model ----------------
class _Node:
    def __init__(self, tag, attrs):
        self.tag, self.attrs, self.kids, self.text = tag, dict(attrs), [], []


class _Tree(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.root = _Node("root", {})
        self.stack = [self.root]

    def handle_starttag(self, tag, attrs):
        if tag in ("table", "tr", "td"):
            n = _Node(tag, attrs)
            self.stack[-1].kids.append(n)
            self.stack.append(n)
        elif tag == "br":
            self.stack[-1].text.append("\n")

    def handle_endtag(self, tag):
        if tag in ("table", "tr", "td"):
            for k in range(len(self.stack) - 1, 0, -1):
                if self.stack[k].tag == tag:
                    del self.stack[k:]
                    break

    def handle_data(self, data):
        self.stack[-1].text.append(data)


def _text(td):
    raw = "".join(td.text).replace("\xa0", " ")
    parts = [re.sub(r"[ \t\r\n]+", " ", p).strip() for p in raw.split("\n")]
    return [p for p in parts if p]


def _find(node, pred):
    if pred(node):
        return node
    for k in node.kids:
        r = _find(k, pred)
        if r:
            return r
    return None


def html_to_model(html_text):
    t = _Tree()
    t.feed(html_text)
    top = _find(t.root, lambda n: n.tag == "table" and n.attrs.get("id") == "TableMessagePrintView")
    if top is None:
        raise ValueError("SWIFT tablosu yok")
    model = {"date": None, "sections": []}
    sec = None
    for tr in [k for k in top.kids if k.tag == "tr"]:
        tds = [k for k in tr.kids if k.tag == "td"]
        if not tds:
            continue
        inner = next((k for k in tds[0].kids if k.tag == "table"), None)
        if inner is None:
            lines = _text(tds[0])
            if not lines:
                continue
            s = " ".join(lines)
            m = SEP_RE.match(s)
            if m:
                sec = {"name": m.group(1).strip(), "lead": [], "rows": []}
                model["sections"].append(sec)
            elif sec is None:
                model["date"] = s
            else:
                sec["lead"].append(s)
            continue
        for itr in [k for k in inner.kids if k.tag == "tr"]:
            cells = [_text(k) for k in itr.kids if k.tag == "td"]
            if not any(cells):
                continue
            label = " ".join(cells[0]) if cells else ""
            colon = " ".join(cells[1]) if len(cells) > 1 else ""
            vals = cells[2] if len(cells) > 2 else []
            if colon == ":":
                sec["rows"].append(("kv", label, vals or [""]))
            else:
                sec["rows"].append(("label", label))
    return model
