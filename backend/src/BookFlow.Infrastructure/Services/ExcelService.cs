using System.Reflection;
using BookFlow.Application.Common.Interfaces;
using ClosedXML.Excel;

namespace BookFlow.Infrastructure.Services;

public sealed class ExcelService : IExcelService
{
    public byte[] Export<T>(string sheetName, IEnumerable<T> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(Truncate(sheetName, 31));
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsPrimitive || p.PropertyType.IsEnum || p.PropertyType == typeof(string) || p.PropertyType == typeof(decimal)
                        || p.PropertyType == typeof(DateTime) || p.PropertyType == typeof(DateOnly) || Nullable.GetUnderlyingType(p.PropertyType) is not null
                        || p.PropertyType == typeof(Guid))
            .ToArray();
        for (var c = 0; c < props.Length; c++)
        {
            ws.Cell(1, c + 1).Value = props[c].Name;
            ws.Cell(1, c + 1).Style.Font.Bold = true;
        }
        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < props.Length; c++)
            {
                var v = props[c].GetValue(row);
                var cell = ws.Cell(r, c + 1);
                switch (v)
                {
                    case null: break;
                    case DateOnly d: cell.Value = d.ToDateTime(TimeOnly.MinValue); cell.Style.DateFormat.Format = "dd-MMM-yyyy"; break;
                    case DateTime dt: cell.Value = dt; cell.Style.DateFormat.Format = "dd-MMM-yyyy HH:mm"; break;
                    case decimal m: cell.Value = m; cell.Style.NumberFormat.Format = "#,##0.00"; break;
                    case int i: cell.Value = i; break;
                    case long l: cell.Value = l; break;
                    case double db: cell.Value = db; break;
                    case bool b: cell.Value = b; break;
                    case Guid g: cell.Value = g.ToString(); break;
                    default: cell.Value = v.ToString(); break;
                }
            }
            r++;
        }
        ws.Columns().AdjustToContents(1, Math.Min(r, 200));
        ws.SheetView.FreezeRows(1);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public List<Dictionary<string, string?>> ReadFirstSheet(Stream stream, int maxRows)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();
        var used = ws.RangeUsed();
        var result = new List<Dictionary<string, string?>>();
        if (used is null) return result;
        var headerRow = used.FirstRow();
        var headers = headerRow.Cells().Select(c => c.GetString().Trim()).ToList();
        foreach (var row in used.RowsUsed().Skip(1))
        {
            if (result.Count >= maxRows) break;
            var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Count; i++)
            {
                if (string.IsNullOrEmpty(headers[i])) continue;
                var cell = row.Cell(i + 1);
                dict[headers[i]] = cell.IsEmpty() ? null : cell.GetFormattedString().Trim();
            }
            if (dict.Values.All(string.IsNullOrWhiteSpace)) continue;
            result.Add(dict);
        }
        return result;
    }

    public byte[] BuildTemplate(string sheetName, string[] headers, string[]? exampleRow = null)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(Truncate(sheetName, 31));
        for (var c = 0; c < headers.Length; c++)
        {
            ws.Cell(1, c + 1).Value = headers[c];
            ws.Cell(1, c + 1).Style.Font.Bold = true;
            ws.Cell(1, c + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
        }
        if (exampleRow is not null)
            for (var c = 0; c < exampleRow.Length && c < headers.Length; c++) ws.Cell(2, c + 1).Value = exampleRow[c];
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];
}
