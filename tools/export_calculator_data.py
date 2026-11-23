"""Exports the code tables from the office calculators into the JSON files SetArch uses.

Run again whenever the spreadsheets are updated:
    python3 tools/export_calculator_data.py <Plumbing_Calculator.xlsx> <travel_distance.xlsx>
Writes src/SetArch.Core/Calculators/Data/plumbing.json and egress.json (values as last calculated by Excel).
"""
import json, os, sys
import openpyxl

OUT = os.path.join(os.path.dirname(__file__), "..", "src", "SetArch.Core", "Calculators", "Data")


def text(v):
    if v is None:
        return ""
    if isinstance(v, float) and v.is_integer():
        return str(int(v))
    return str(v)


def num(v):
    return float(v) if isinstance(v, (int, float)) and not isinstance(v, bool) else 0.0


def plumbing(path):
    ws = openpyxl.load_workbook(path, data_only=True)["Code Data"]
    occupancies = []
    for r in range(3, 42):
        g = lambda col: ws[f"{col}{r}"].value
        occupancies.append({
            "Key": text(g("AA")),
            "Classification": text(g("A")),
            "Description": text(g("B")),
            "Basis": text(g("C")),
            "WcMale": [num(g("G")), num(g("H")), num(g("I"))],
            "WcFemale": [num(g("J")), num(g("K")), num(g("L"))],
            "LavMale": [num(g("M")), num(g("N")), num(g("O"))],
            "LavFemale": [num(g("P")), num(g("Q")), num(g("R"))],
            "BathPer": num(g("S")),
            "FountainPer": num(g("V")),
            "ServiceSink": text(g("W")) or "None",
            "SexSeparated": bool(g("X")),
            "FountainExceptionE": bool(g("Y")),
        })
    functions = []
    for r in range(45, 107):
        name = ws[f"A{r}"].value
        if not name:
            continue
        functions.append({
            "Name": text(name),
            "Factor": num(ws[f"B{r}"].value),
            "Basis": text(ws[f"C{r}"].value),
            "Occupancy": text(ws[f"D{r}"].value),
        })
    return {"Source": os.path.basename(path), "Occupancies": occupancies, "Functions": functions}


def egress(path):
    wb = openpyxl.load_workbook(path, data_only=True)
    rt, calc = wb["Resolved Table"], wb["Egress Calculator"]
    rows = {r: [text(rt.cell(r, c).value) for c in range(1, 22)] for r in range(4, 51)}
    fbc = [{"Name": text(calc[f"A{r}"].value), "Row": int(calc[f"B{r}"].value)} for r in range(26, 42) if calc[f"A{r}"].value]
    ffpc = [{"Name": text(calc[f"D{r}"].value), "Row": int(calc[f"E{r}"].value)} for r in range(26, 53) if calc[f"D{r}"].value]
    metrics = [{"Name": text(calc[f"G{r}"].value), "Unsprinklered": int(calc[f"H{r}"].value), "Sprinklered": int(calc[f"I{r}"].value)} for r in range(26, 29)]
    notes = [{"Id": text(calc[f"G{r}"].value), "Text": text(calc[f"H{r}"].value).strip()} for r in range(34, 50)]
    return {"Source": os.path.basename(path), "Rows": rows, "Fbc": fbc, "Ffpc": ffpc, "Metrics": metrics, "Footnotes": notes}


if __name__ == "__main__":
    plumbing_xlsx, egress_xlsx = sys.argv[1], sys.argv[2]
    os.makedirs(OUT, exist_ok=True)
    for name, data in (("plumbing.json", plumbing(plumbing_xlsx)), ("egress.json", egress(egress_xlsx))):
        with open(os.path.join(OUT, name), "w", encoding="utf-8") as f:
            json.dump(data, f, indent=1, ensure_ascii=False)
        print("wrote", name)
