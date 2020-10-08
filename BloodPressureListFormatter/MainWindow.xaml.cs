using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace BloodPressureListFormatter
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        private static IDictionary<string, int> headerMap;

        public MainWindow()
        {
            InitializeComponent();
            Task.Run(() =>
            {
                Dispatcher.Invoke(() =>
                {
                    foreach (var fileName in Environment.GetCommandLineArgs().Skip(1))
                        GenerateDocument(fileName);
                });
            });
        }

        protected override void OnPreviewDragOver(DragEventArgs e)
        {
            base.OnPreviewDragOver(e);
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        protected override void OnPreviewDrop(DragEventArgs e)
        {
            base.OnPreviewDrop(e);
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] fileNames = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (fileNames != null)
                {
                    foreach (var fileName in fileNames)
                        GenerateDocument(fileName);
                }
            }
        }

        private void GenerateDocument(string fileName)
        {
            if (File.Exists(fileName))
            {
                var outputFilePath = Path.Combine(Path.GetDirectoryName(fileName), "output.html");
                GenerateDocument(fileName, outputFilePath);
                System.Diagnostics.Process.Start(outputFilePath);
            }
        }

        private void GenerateDocument(string inputfilePath, string outputFilePath)
        {
            try
            {
                var templatePath = Path.Combine(Path.GetDirectoryName(GetType().Assembly.Location), "PageTemplate.html");
                var headerTemplate = "";
                var contentTemplate = "";
                var delimiterTemplate = "";
                var footerTemplate = "";
                using (var reader = new StreamReader(templatePath, Encoding.UTF8))
                {
                    var headerTempleteSB = new StringBuilder();
                    var contentTempleteSB = new StringBuilder();
                    var delimiterTempleteSB = new StringBuilder();
                    var footerTempleteSB = new StringBuilder();
                    var targetSB = headerTempleteSB;
                    while (!reader.EndOfStream)
                    {
                        var text = reader.ReadLine();
                        if (text == null)
                            break;
                        switch (text.Trim())
                        {
                            case "<!--END OF HEADER-->":
                                if (targetSB != headerTempleteSB)
                                    throw new Exception();
                                targetSB = null;
                                break;
                            case "<!--START OF DELIMITER-->":
                                if (targetSB != null)
                                    throw new Exception();
                                targetSB = delimiterTempleteSB;
                                break;
                            case "<!--END OF DELIMITER-->":
                                if (targetSB != delimiterTempleteSB)
                                    throw new Exception();
                                targetSB = null;
                                break;
                            case "<!--START OF CONTENT-->":
                                if (targetSB != null)
                                    throw new Exception();
                                targetSB = contentTempleteSB;
                                break;
                            case "<!--END OF CONTENT-->":
                                if (targetSB != contentTempleteSB)
                                    throw new Exception();
                                targetSB = null;
                                break;
                            case "<!--START OF FOOTER-->":
                                if (targetSB != null)
                                    throw new Exception();
                                targetSB = footerTempleteSB;
                                break;
                            default:
                                targetSB.Append(text);
                                targetSB.Append("\n");
                                break;
                        }
                    }
                    if (targetSB != footerTempleteSB)
                        throw new Exception();
                    headerTemplate = headerTempleteSB.ToString();
                    contentTemplate = contentTempleteSB.ToString();
                    delimiterTemplate = delimiterTempleteSB.ToString();
                    footerTemplate = footerTempleteSB.ToString();
                }
                var contentTemplateValuePattern = new Regex(@"\${(?<keyword>[^_}]+)(_(?<day>[0-6\*]))?}", RegexOptions.Compiled);
                using (var writer = new StreamWriter(outputFilePath, false, Encoding.UTF8))
                using (var parser = new CSVParser(inputfilePath, Encoding.GetEncoding("shift-jis"), CSVDelimiter.COMMA))
                {
                    writer.Write(headerTemplate);
                    var rows = parser.GetRows();
                    var header = rows.First();
                    headerMap = Enumerable.Range(0, header.size).Select(index => new { index, title = header.getString(index) }).Where(item => !string.IsNullOrEmpty(item.title)).ToDictionary(item => item.title, item => item.index);
                    var dataRows = rows.Skip(1).ToDictionary(row => row.getDate(headerMap["日付"]), row => row);
                    var rowsOfWeeks = new List<ICSVRow>();
                    var firstDate = dataRows.Keys.Min();
                    var lastDate = dataRows.Keys.Max();
                    var isFirstPage = true;
                    for (var date = firstDate; date <= lastDate; date += TimeSpan.FromDays(7))
                    {
                        if (!isFirstPage)
                            writer.Write(delimiterTemplate);
                        var rowsOfWeek =
                            Enumerable.Range(0, 7)
                            .Select(days =>
                            {
                                ICSVRow row;
                                if (!dataRows.TryGetValue(date + TimeSpan.FromDays(days), out row))
                                    row = null;
                                return row;
                            })
                            .ToArray();
                        var content =
                            contentTemplateValuePattern.Replace(
                                contentTemplate,
                                m => GetContentTemplateValue(rowsOfWeek, m));
                        writer.Write(content);
                        isFirstPage = false;
                    }
                    writer.Write(footerTemplate);
                }
            }
            catch (Exception)
            {
            }
        }

        private string GetContentTemplateValue(ICSVRow[] rowsOfWeek, Match m)
        {
            var keyword = m.Groups["keyword"].Value;
            int dayOfWeek = -1;
            if (m.Groups["day"].Success)
            {
                var dayOfWeekString = m.Groups["day"].Value;
                if (!int.TryParse(dayOfWeekString, out dayOfWeek))
                    dayOfWeek = -1;
            }
            switch (keyword)
            {
                case "開始日":
                    if (dayOfWeek >= 0)
                        throw new Exception();
                    if (rowsOfWeek[0] == null)
                        return "";
                    if (rowsOfWeek[0] == null)
                        return "";
                    return rowsOfWeek[0].getDate(headerMap["日付"])?.ToString("yyyy年M月d日") ?? "";
                case "日付":
                    if (dayOfWeek < 0)
                        throw new Exception();
                    if (rowsOfWeek[dayOfWeek] == null)
                        return "/";
                    return rowsOfWeek[dayOfWeek].getDate(headerMap["日付"])?.ToString("M/d") ?? "/";
                case "曜日":
                    if (dayOfWeek < 0)
                        throw new Exception();
                    if (rowsOfWeek[dayOfWeek] == null)
                        return "&nbsp;&nbsp;";
                    return rowsOfWeek[dayOfWeek].getDate(headerMap["日付"])?.ToString("ddd") ?? "&nbsp;&nbsp;";
                case "最高血圧朝1":
                    if (dayOfWeek < 0)
                        throw new Exception();
                    if (rowsOfWeek[dayOfWeek] == null)
                        return "";
                    return rowsOfWeek[dayOfWeek].getDoule(headerMap["最高血圧（朝）"])?.ToString("F0") ?? "";
                case "最高血圧朝2":
                    return "";
                case "最高血圧朝平均":
                    if (dayOfWeek < 0)
                    {
                        var rows = rowsOfWeek.Where(row => row != null);
                        if (rows.Any())
                            return rows.Average(row => row.getDoule(headerMap["最高血圧（朝）"]))?.ToString("F0") ?? "";
                        else
                            return "";
                    }
                    else
                    {
                        if (rowsOfWeek[dayOfWeek] == null)
                            return "";
                        return rowsOfWeek[dayOfWeek].getDoule(headerMap["最高血圧（朝）"])?.ToString("F0") ?? "";
                    }
                case "最高血圧夜1":
                    if (dayOfWeek < 0)
                        throw new Exception();
                    if (rowsOfWeek[dayOfWeek] == null)
                        return "";
                    return rowsOfWeek[dayOfWeek].getDoule(headerMap["最高血圧（夜）"])?.ToString("F0") ?? "";
                case "最高血圧夜2":
                    return "";
                case "最高血圧夜平均":
                    if (dayOfWeek < 0)
                    {
                        var rows = rowsOfWeek.Where(row => row != null);
                        if (rows.Any())
                            return rows.Average(row => row.getDoule(headerMap["最高血圧（夜）"]))?.ToString("F0") ?? "";
                        else
                            return "";
                    }
                    else
                    {
                        if (rowsOfWeek[dayOfWeek] == null)
                            return "";
                        return rowsOfWeek[dayOfWeek].getDoule(headerMap["最高血圧（夜）"])?.ToString("F0") ?? "";
                    }
                case "最低血圧朝1":
                    if (dayOfWeek < 0)
                        throw new Exception();
                    if (rowsOfWeek[dayOfWeek] == null)
                        return "";
                    return rowsOfWeek[dayOfWeek].getDoule(headerMap["最低血圧（朝）"])?.ToString("F0") ?? "";
                case "最低血圧朝2":
                    return "";
                case "最低血圧朝平均":
                    if (dayOfWeek < 0)
                    {
                        var rows = rowsOfWeek.Where(row => row != null);
                        if (rows.Any())
                            return rows.Average(row => row.getDoule(headerMap["最低血圧（朝）"]))?.ToString("F0") ?? "";
                        else
                            return "";
                    }
                    else
                    {
                        if (rowsOfWeek[dayOfWeek] == null)
                            return "";
                        return rowsOfWeek[dayOfWeek].getDoule(headerMap["最低血圧（朝）"])?.ToString("F0") ?? "";
                    }
                case "最低血圧夜1":
                    if (dayOfWeek < 0)
                        throw new Exception();
                    if (rowsOfWeek[dayOfWeek] == null)
                        return "";
                    return rowsOfWeek[dayOfWeek].getDoule(headerMap["最低血圧（夜）"])?.ToString("F0") ?? "";
                case "最低血圧夜2":
                    return "";
                case "最低血圧夜平均":
                    if (dayOfWeek < 0)
                    {
                        var rows = rowsOfWeek.Where(row => row != null);
                        if (rows.Any())
                            return rows.Average(row => row.getDoule(headerMap["最低血圧（夜）"]))?.ToString("F0") ?? "";
                        else
                            return "";
                    }
                    else
                    {
                        if (rowsOfWeek[dayOfWeek] == null)
                            return "";
                        return rowsOfWeek[dayOfWeek].getDoule(headerMap["最低血圧（夜）"])?.ToString("F0") ?? "";
                    }
                case "脈拍1":
                    if (dayOfWeek < 0)
                        throw new Exception();
                    if (rowsOfWeek[dayOfWeek] == null)
                        return "";
                    {
                        var validValues =
                            new[]
                            {
                                rowsOfWeek[dayOfWeek].getDoule(headerMap["心拍（朝）"]),
                                rowsOfWeek[dayOfWeek].getDoule(headerMap["心拍（夜）"]),
                            }
                            .Where(n => n.HasValue);
                        if (!validValues.Any())
                            return "";
                        return
                            (validValues.Average(n => n.Value) + 0.5).ToString("F0");
                    }
                case "脈拍2":
                    return "";
                default:
                    throw new Exception();
            }
        }
    }
}
