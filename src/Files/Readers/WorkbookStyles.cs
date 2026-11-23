using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using ExcelNumberFormat;

namespace Dafeiyu
{
    internal sealed class WorkbookStyles
    {
        static readonly Dictionary<int, string> BuiltIns = new Dictionary<int, string>
        {
            {0, "General"}, {1, "0"}, {2, "0.00"}, {3, "#,##0"}, {4, "#,##0.00"},
            {5, "\"$\"#,##0_);(\"$\"#,##0)"}, {6, "\"$\"#,##0_);[Red](\"$\"#,##0)"},
            {7, "\"$\"#,##0.00_);(\"$\"#,##0.00)"}, {8, "\"$\"#,##0.00_);[Red](\"$\"#,##0.00)"},
            {9, "0%"}, {10, "0.00%"}, {11, "0.00E+00"}, {12, "# ?/?"}, {13, "# ??/??"},
            {14, "m/d/yy"}, {15, "d-mmm-yy"}, {16, "d-mmm"}, {17, "mmm-yy"},
            {18, "h:mm AM/PM"}, {19, "h:mm:ss AM/PM"}, {20, "h:mm"}, {21, "h:mm:ss"}, {22, "m/d/yy h:mm"},
            {37, "#,##0 ;(#,##0)"}, {38, "#,##0 ;[Red](#,##0)"}, {39, "#,##0.00;(#,##0.00)"}, {40, "#,##0.00;[Red](#,##0.00)"},
            {41, "_(* #,##0_);_(* (#,##0);_(* \"-\"_);_(@_)"},
            {42, "_(\"$\"* #,##0_);_(\"$\"* (#,##0);_(\"$\"* \"-\"_);_(@_)"},
            {43, "_(* #,##0.00_);_(* (#,##0.00);_(* \"-\"??_);_(@_)"},
            {44, "_(\"$\"* #,##0.00_);_(\"$\"* (#,##0.00);_(\"$\"* \"-\"??_);_(@_)"},
            {45, "mm:ss"}, {46, "[h]:mm:ss"}, {47, "mmss.0"}, {48, "##0.0E+0"}, {49, "@"}
        };
        readonly Dictionary<int, string> numberFormats = new Dictionary<int, string>();
        readonly List<string> fills = new List<string>();
        readonly List<Style> cellStyles = new List<Style>();
        readonly List<Style> styleDefaults = new List<Style>();
        readonly Dictionary<string, NumberFormat> parsed = new Dictionary<string, NumberFormat>(StringComparer.Ordinal);

        internal static WorkbookStyles Read(WorkbookPackage package, string path)
        {
            var styles = new WorkbookStyles();
            if (path == null) return styles;
            int groupDepth = -1;
            string group = null;
            using (var xml = package.OpenXml(path))
                while (package.Read(xml))
                {
                    if (!WorkbookPackage.IsSpreadsheet(xml)) continue;
                    if (xml.NodeType == XmlNodeType.EndElement && xml.Depth == groupDepth) { group = null; groupDepth = -1; }
                    if (xml.NodeType != XmlNodeType.Element) continue;
                    if (xml.Depth == 1 && !xml.IsEmptyElement) { group = xml.LocalName; groupDepth = xml.Depth; continue; }
                    if (groupDepth < 0 || xml.Depth != groupDepth + 1) continue;
                    if (group == "numFmts" && xml.LocalName == "numFmt")
                    {
                        int id = WorkbookPackage.Integer(xml.GetAttribute("numFmtId"), -1);
                        string format = xml.GetAttribute("formatCode");
                        if (id >= 0 && format != null && format.Length <= 1024) styles.numberFormats[id] = format;
                    }
                    else if (group == "fills" && xml.LocalName == "fill") styles.fills.Add(ReadFill(package, xml));
                    else if (xml.LocalName == "xf" && (group == "cellXfs" || group == "cellStyleXfs"))
                    {
                        var style = new Style {
                            NumberFormat = WorkbookPackage.Integer(xml.GetAttribute("numFmtId"), -1),
                            Fill = WorkbookPackage.Integer(xml.GetAttribute("fillId"), -1),
                            BaseStyle = WorkbookPackage.Integer(xml.GetAttribute("xfId"), -1),
                            UseBaseNumberFormat = IsFalse(xml.GetAttribute("applyNumberFormat")),
                            UseBaseFill = IsFalse(xml.GetAttribute("applyFill"))
                        };
                        (group == "cellXfs" ? styles.cellStyles : styles.styleDefaults).Add(style);
                    }
                }
            return styles;
        }

        static bool IsFalse(string value) { return value == "0" || value == "false"; }

        static string ReadFill(WorkbookPackage package, XmlReader reader)
        {
            string pattern = null, foreground = null, background = null;
            var gradient = new List<string>();
            using (var fill = reader.ReadSubtree())
                while (package.Read(fill))
                {
                    if (fill.NodeType != XmlNodeType.Element || !WorkbookPackage.IsSpreadsheet(fill)) continue;
                    if (fill.LocalName == "patternFill") pattern = fill.GetAttribute("patternType");
                    else if (fill.LocalName == "fgColor") foreground = Color(fill);
                    else if (fill.LocalName == "bgColor") background = Color(fill);
                    else if (fill.LocalName == "gradientFill") pattern = "gradient";
                    else if (pattern == "gradient" && fill.Depth == 3 && gradient.Count < 100)
                    {
                        string color = Color(fill);
                        if (color != null) gradient.Add(color);
                    }
                }
            if (pattern == "solid") return foreground;
            if (pattern == "gradient") return "gradient " + String.Join(",", gradient.ToArray());
            if (pattern == null || pattern == "none") return null;
            return pattern + " fg=" + foreground + " bg=" + background;
        }

        static string Color(XmlReader reader)
        {
            string color = reader.GetAttribute("rgb");
            string theme = reader.GetAttribute("theme"), indexed = reader.GetAttribute("indexed"), auto = reader.GetAttribute("auto");
            if (color == null && theme != null) color = "theme=" + theme;
            if (color == null && indexed != null) color = "indexed=" + indexed;
            if (color == null && (auto == "1" || auto == "true")) color = "auto";
            string tint = reader.GetAttribute("tint");
            return color != null && tint != null ? color + " tint=" + tint : color;
        }

        internal void Lookup(int index, out string numberFormat, out string fill)
        {
            var xf = index >= 0 && index < cellStyles.Count ? cellStyles[index] : null;
            var style = xf != null && xf.BaseStyle >= 0 && xf.BaseStyle < styleDefaults.Count ? styleDefaults[xf.BaseStyle] : null;
            int numId = xf == null ? 0 : xf.NumberFormat;
            int fillId = xf == null ? -1 : xf.Fill;
            if (style != null)
            {
                if (numId < 0 || xf.UseBaseNumberFormat) numId = style.NumberFormat;
                if (fillId < 0 || xf.UseBaseFill) fillId = style.Fill;
            }
            if (numId < 0) numId = 0;
            if (!numberFormats.TryGetValue(numId, out numberFormat) && !BuiltIns.TryGetValue(numId, out numberFormat))
                numberFormat = "numFmtId=" + numId;
            fill = fillId >= 0 && fillId < fills.Count ? fills[fillId] : null;
        }

        internal string Display(string value, string format, object input, bool date1904)
        {
            if (input == null || String.IsNullOrEmpty(format) || format == "General") return value;
            NumberFormat parser;
            if (!parsed.TryGetValue(format, out parser))
            {
                if (parsed.Count >= 256) parsed.Clear();
                parser = new NumberFormat(format);
                parsed.Add(format, parser);
            }
            if (!parser.IsValid) return value;
            try { return parser.Format(input, CultureInfo.InvariantCulture, date1904); }
            catch (ArgumentException) { return value; }
            catch (OverflowException) { return value; }
        }

        sealed class Style
        {
            internal int NumberFormat, Fill, BaseStyle;
            internal bool UseBaseNumberFormat, UseBaseFill;
        }
    }
}
