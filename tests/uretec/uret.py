# -*- coding: utf-8 -*-
"""Uydurma test vakalarını üretir: tests/vakalar/<vaka>.html, tests/beklenen/*.txt, tests/vakalar/kunye.json.

Bütün veriler hayalidir (ornek-ham.txt). Gerçek mail ya da gerçek PDF depoya girmez.
Kullanım: py -3 tests/uretec/uret.py
"""
import copy
import json
import pathlib
import re
import sys

BURA = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(BURA))
import swift_html as S  # noqa: E402

TESTS = BURA.parent
VAKA = TESTS / "vakalar"
BEK = TESTS / "beklenen"
SABLON = (BURA / "sablon.html").read_text(encoding="utf-8")

# Sayfa düzenleri (dar sütun, sayfa sınırı) tests/run.ps1'de uygulanır: SWIFT tablosu işaretler arasında durur.
BASI, SONU = "<!--SWIFT-TABLO-BASI-->", "<!--SWIFT-TABLO-SONU-->"


def sec(m, ad):
    return next(x for x in m["sections"] if x["name"] == ad)


def bul(rows, label, after=None, kind=None):
    start = 0
    if after:
        start = next(i for i, r in enumerate(rows) if r[1] == after) + 1
    for i in range(start, len(rows)):
        if rows[i][1] == label and (kind is None or rows[i][0] == kind):
            return i
    raise KeyError(label)


def kv_degis(m, label, deger, after=None, bolum="Message Text"):
    rows = sec(m, bolum)["rows"]
    i = bul(rows, label, after, "kv")
    vals = list(rows[i][2])
    vals[0] = deger
    rows[i] = ("kv", label, vals)


def ekle(m, once_label, yeni, after=None, bolum="Message Text"):
    rows = sec(m, bolum)["rows"]
    i = bul(rows, once_label, after)
    rows[i:i] = yeni


def L(t):
    return ("label", t)


def KV(a, *v):
    return ("kv", a, list(v) if v else [""])


def kunye(m):
    def get(label):
        for sx in m["sections"]:
            for r in sx["rows"]:
                if r[0] == "kv" and r[1] == label:
                    return r[2][0]
        return ""
    amt = get("Instructed Amount") or get("Interbank Settlement Amount")
    mm = re.search(r"#?([A-Z]{3})\s*([0-9][0-9.,]*)#?", amt)
    return {"ref": get("Message Identification") or get("Instruction Identification"),
            "tarih": get("Interbank Settlement Date"),
            "tutar": mm.group(2) if mm else "", "doviz": mm.group(1) if mm else ""}


def vakalar():
    temel = S.ham_to_model((BURA / "ornek-ham.txt").read_text(encoding="utf-8"))
    v = {}

    def yeni(ad):
        v[ad] = copy.deepcopy(temel)
        return v[ad]

    yeni("t01-temel")

    m = yeni("t02-uzun-alici")
    kv_degis(m, "Name", "EXAMPLE INTERNATIONAL INDUSTRIAL MACHINERY AND EQUIPMENT TRADING COMPANY LIMITED", after="Creditor")

    m = yeni("t03-uzun-banka")
    rows = sec(m, "Message Text")["rows"]
    i = bul(rows, "BICFI", "Creditor Agent", "kv")
    rows[i] = ("kv", "BICFI", ["EXMPCNSHXXX", "( SHANGHAI EXAMPLE DEVELOPMENT BANK CO LTD INTERNATIONAL SETTLEMENT DEPARTMENT HEAD OFFICE)"])

    m = yeni("t04-adres")
    rows = sec(m, "Message Text")["rows"]
    i = bul(rows, "Address Line", "Creditor", "kv")
    rows[i:i + 1] = [KV("Address Line", "UNIT 1 EXAMPLE TOWER 2 SAMPLE CENTRE"), KV("Address Line", "1 EXAMPLE ROAD"),
                     KV("Address Line", "HONG KONG SAR"), KV("Post Code", "000000"), KV("Town Name", "HONG KONG"),
                     KV("Country Sub Division", "HONG KONG ISLAND"), KV("Country", "HK")]

    m = yeni("t05-araci-banka")
    ekle(m, "Creditor Agent", [L("Intermediary Agent1"), L("Financial Institution Identification"),
                                KV("BICFI", "EXMPUS33XXX", "( NEW YORK,NY EXAMPLE BANK, N.A.)"),
                                L("Clearing System Member Identification"), L("Clearing System Identification"),
                                KV("Code", "USABA"), KV("Member Identification", "000000001")])

    m = yeni("t06-diger-hesap")
    rows = sec(m, "Message Text")["rows"]
    i = bul(rows, "IBAN", "Creditor Account", "kv")
    rows[i:i + 1] = [L("Other"), KV("Identification", "0000000000000000001"), L("Scheme Name"), KV("Proprietary", "CUID")]

    m = yeni("t07-uzun-aciklama")
    kv_degis(m, "Unstructured", "PAYMENT FOR INVOICE NO 2026-INV-00001 AND 2026-INV-00002 CONTRACT EX-2026-001 "
                                "ADVANCE 30 PERCENT GOODS TEXTILE MACHINERY SPARE PARTS")

    # Boşluksuz çok uzun değer tabloyu sayfadan taşırır; bazı çizimler kenardaki harfleri atar.
    # Beklenen: ya metin birebir, ya program "kesilmiş olabilir" uyarısı verir.
    m = yeni("t08-bosluksuz")
    kv_degis(m, "Unstructured", "INV2026000001INV2026000002CONTRACTEX2026001ADVANCEPAYMENTXYZ0123456789")

    m = yeni("t09-amac-bildirim")
    ekle(m, "Instructing Agent", [L("Charges Information"), KV("Amount", "#USD25,00#"), L("Agent"),
                                   L("Financial Institution Identification"),
                                   KV("BICFI", "KTEFTRISXXX", "( ISTANBUL KUVEYT TURK KATILIM BANKASI A.S.)")])
    sec(m, "Message Text")["rows"] += [L("Purpose"), KV("Code", "GDDS"), L("Regulatory Reporting"), L("Details"),
                                       KV("Code", "TR01"), KV("Information", "IMPORT OF GOODS TEXTILE")]

    m = yeni("t10-talimat")
    ekle(m, "Remittance Information", [L("Instruction For Creditor Agent"),
                                       KV("Instruction Information", "PLEASE ADVISE BENEFICIARY BY PHONE 0000 000 000 00 00")])

    m = yeni("t11-nihai-taraf")
    ekle(m, "Creditor Agent", [L("Ultimate Debtor"), KV("Name", "ORNEK DIS TICARET LIMITED SIRKETI"),
                               L("Identification"), L("Organisation Identification"), KV("Any BIC", "ORNKTRIS")])
    sec(m, "Message Text")["rows"] += [L("Ultimate Creditor"), KV("Name", "SAMPLE GLOBAL TRADING FZE"),
                                       L("Identification"), L("Organisation Identification"), KV("LEI", "00000000000000000001")]

    for kod, tutar in [("jpy", "#JPY1.250.000#"), ("usd", "#USD1.234.567,89#")]:
        m = yeni(f"t12-{kod}")
        kv_degis(m, "Interbank Settlement Amount", tutar)
        kv_degis(m, "Instructed Amount", tutar)

    m = yeni("t13-turkce")
    kv_degis(m, "Name", "ÇAĞLAYAN İŞ MAKİNELERİ SANAYİ VE TİCARET ŞİRKETİ", after="Debtor")
    kv_degis(m, "Street Name", "GÜNEŞLİ MAH. ÇINAR SK.", after="Debtor")

    m = yeni("t14-iki-mudahale")
    sec(m, "Interventions")["rows"] += [KV("Category", "Delivery Notification"),
                                        KV("Creation Time (Paygate Maestro)", "15/01/2026 10:20:00"),
                                        KV("Application", "SWIFT Interface"),
                                        KV("Text", "{1:F21KTEFTRISAXXX0000000000002}{4:{177:2601151020}{451:0}}")]

    m = yeni("t15-bos-deger")
    kv_degis(m, "Address Line", "", after="Creditor")

    m = yeni("t16-iki-nokta")
    kv_degis(m, "Unstructured", ":/ROC/INV-2026-001")

    m = yeni("t17-parantez")
    kv_degis(m, "Unstructured", "(INV 001) ADVANCE PAYMENT FOR GOODS AND SERVICES CONTRACT 2026 001 TEXTILE")

    m = yeni("t18-cok-uzun")
    blok = [L("Intermediary Agent1"), L("Financial Institution Identification"),
            KV("BICFI", "EXMPUS33XXX", "( NEW YORK,NY EXAMPLE BANK, N.A.)"),
            L("Clearing System Member Identification"), L("Clearing System Identification"), KV("Code", "USABA"),
            KV("Member Identification", "000000001"), L("Intermediary Agent1 Account"), L("Identification"),
            L("Other"), KV("Identification", "000000000001")]
    for _ in range(4):
        ekle(m, "Creditor Agent", copy.deepcopy(blok))
    sec(m, "Message Text")["rows"] += [L("Purpose"), KV("Code", "GDDS"), L("Regulatory Reporting"), L("Details"),
                                       KV("Code", "TR01"), KV("Information", "IMPORT OF GOODS TEXTILE MACHINERY")]
    return v


def main():
    for d in (VAKA, BEK):
        d.mkdir(exist_ok=True)
        for f in d.glob("*.*"):
            f.unlink()
    kunyeler, sayi = {}, 0
    for ad, m in vakalar().items():
        h = S.replace_table(SABLON.replace("{TABLO}", "<table id=TableMessagePrintView></table>"), m)
        geri = S.html_to_model(h)
        assert S.expected_lines(geri) == S.expected_lines(m), ad
        (BEK / f"{ad}.txt").write_text("\n".join(S.expected_lines(m)) + "\n", encoding="utf-8")
        kunyeler[ad] = kunye(m)
        a, b = S.table_span(h)
        (VAKA / f"{ad}.html").write_text(h[:a] + BASI + h[a:b] + SONU + h[b:], encoding="utf-8")
        sayi += 1
    kunyeler["_kesik_olabilir"] = ["t08-bosluksuz"]
    (VAKA / "kunye.json").write_text(json.dumps(kunyeler, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"vaka: {len(kunyeler) - 1}  html: {sayi}")


if __name__ == "__main__":
    main()
