using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using DateCalc.Internal;

namespace DateCalc.Holidays
{
    /// <summary>
    /// 内閣府公開の「国民の祝日について」（syukujitsu.csv 形式: YYYY/M/D,名称）等を
    /// ローカルファイル・ストリーム・文字列から読み込むオフライン祝日プロバイダです。
    /// ネットワーク通信は一切行いません。
    /// </summary>
    public sealed class CabinetOfficeCsvHolidayProvider : IHolidayProvider
    {
        private readonly Dictionary<DateTime, HolidayInfo> _holidaysByDate;
        private readonly Dictionary<int, IReadOnlyList<HolidayInfo>> _holidaysByYear;

        private CabinetOfficeCsvHolidayProvider(
            Dictionary<DateTime, HolidayInfo> holidaysByDate,
            Dictionary<int, IReadOnlyList<HolidayInfo>> holidaysByYear,
            int? minYear,
            int? maxYear)
        {
            _holidaysByDate = holidaysByDate;
            _holidaysByYear = holidaysByYear;
            MinYear = minYear;
            MaxYear = maxYear;
        }

        /// <summary>
        /// 読み込まれたデータに含まれる最小西暦年を取得します（データが 0 件の場合は <c>null</c>）。
        /// </summary>
        public int? MinYear { get; }

        /// <summary>
        /// 読み込まれたデータに含まれる最大西暦年を取得します（データが 0 件の場合は <c>null</c>）。
        /// </summary>
        public int? MaxYear { get; }

        /// <summary>
        /// 読み込まれた祝日の総件数を取得します。
        /// </summary>
        public int Count => _holidaysByDate.Count;

        /// <summary>
        /// ローカルの CSV ファイルから祝日プロバイダを作成します。
        /// </summary>
        /// <param name="filePath">CSV ファイルのパス。</param>
        /// <param name="encoding">文字エンコーディング。省略時は UTF-8（BOM 自動判定あり）を使用します。</param>
        /// <returns>作成された <see cref="CabinetOfficeCsvHolidayProvider"/>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="filePath"/> が <c>null</c> の場合にスローされます。</exception>
        public static CabinetOfficeCsvHolidayProvider FromFile(string filePath, Encoding? encoding = null)
        {
            if (filePath == null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return FromStream(stream, encoding);
        }

        /// <summary>
        /// ストリームから祝日プロバイダを作成します。
        /// </summary>
        /// <param name="stream">CSV データを含むストリーム。</param>
        /// <param name="encoding">文字エンコーディング。省略時は UTF-8（BOM 自動判定あり）を使用します。</param>
        /// <returns>作成された <see cref="CabinetOfficeCsvHolidayProvider"/>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> が <c>null</c> の場合にスローされます。</exception>
        public static CabinetOfficeCsvHolidayProvider FromStream(Stream stream, Encoding? encoding = null)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            using var reader = encoding != null
                ? new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true)
                : new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);

            return FromReader(reader);
        }

        /// <summary>
        /// CSV 文字列から祝日プロバイダを作成します。
        /// </summary>
        /// <param name="csvContent">CSV 形式のテキスト。</param>
        /// <returns>作成された <see cref="CabinetOfficeCsvHolidayProvider"/>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="csvContent"/> が <c>null</c> の場合にスローされます。</exception>
        public static CabinetOfficeCsvHolidayProvider FromText(string csvContent)
        {
            if (csvContent == null)
            {
                throw new ArgumentNullException(nameof(csvContent));
            }

            using var reader = new StringReader(csvContent);
            return FromReader(reader);
        }

        /// <summary>
        /// <see cref="TextReader"/> から CSV データを読み込んで祝日プロバイダを作成します。
        /// </summary>
        /// <param name="reader">読み取り元の <see cref="TextReader"/>。</param>
        /// <returns>作成された <see cref="CabinetOfficeCsvHolidayProvider"/>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="reader"/> が <c>null</c> の場合にスローされます。</exception>
        /// <exception cref="FormatException">ヘッダ行以外のデータ行の形式が不正な場合にスローされます。</exception>
        public static CabinetOfficeCsvHolidayProvider FromReader(TextReader reader)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            var byDate = new Dictionary<DateTime, HolidayInfo>();
            var byYearTemp = new Dictionary<int, List<HolidayInfo>>();
            int? minYear = null;
            int? maxYear = null;
            bool isFirstNonEmptyLine = true;
            int lineNumber = 0;

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                string trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (!TrySplitCsvLine(trimmed, out string dateField, out string nameField))
                {
                    if (isFirstNonEmptyLine)
                    {
                        isFirstNonEmptyLine = false;
                        continue;
                    }

                    throw new FormatException($"CSV の {lineNumber} 行目の形式が不正です: '{line}'");
                }

                if (!TimeFormatHelper.TryParseDate(dateField, out DateTime date))
                {
                    if (isFirstNonEmptyLine)
                    {
                        // 先頭のヘッダ行（例: "国民の祝日・休日月日,国民の祝日・休日名称"）はスキップする
                        isFirstNonEmptyLine = false;
                        continue;
                    }

                    throw new FormatException($"CSV の {lineNumber} 行目の日付形式が不正です: '{dateField}'");
                }

                isFirstNonEmptyLine = false;
                if (string.IsNullOrWhiteSpace(nameField))
                {
                    throw new FormatException($"CSV の {lineNumber} 行目の祝日名が空です。");
                }

                HolidayKind kind = ClassifyHolidayKind(nameField);
                var info = new HolidayInfo(date, nameField, kind);
                byDate[date] = info;

                if (!minYear.HasValue || date.Year < minYear.Value)
                {
                    minYear = date.Year;
                }

                if (!maxYear.HasValue || date.Year > maxYear.Value)
                {
                    maxYear = date.Year;
                }
            }

            foreach (HolidayInfo info in byDate.Values)
            {
                if (!byYearTemp.TryGetValue(info.Date.Year, out List<HolidayInfo>? list))
                {
                    list = new List<HolidayInfo>();
                    byYearTemp.Add(info.Date.Year, list);
                }

                list.Add(info);
            }

            var byYear = new Dictionary<int, IReadOnlyList<HolidayInfo>>(byYearTemp.Count);
            foreach (KeyValuePair<int, List<HolidayInfo>> entry in byYearTemp)
            {
                entry.Value.Sort((left, right) => left.Date.CompareTo(right.Date));
                byYear.Add(entry.Key, new ReadOnlyCollection<HolidayInfo>(entry.Value.ToArray()));
            }

            return new CabinetOfficeCsvHolidayProvider(byDate, byYear, minYear, maxYear);
        }

        /// <inheritdoc />
        public bool TryGetHoliday(DateTime date, out HolidayInfo? holiday)
        {
            return _holidaysByDate.TryGetValue(date.Date, out holiday);
        }

        /// <inheritdoc />
        public IReadOnlyList<HolidayInfo> GetHolidays(int year)
        {
            return _holidaysByYear.TryGetValue(year, out IReadOnlyList<HolidayInfo>? list)
                ? list
                : Array.Empty<HolidayInfo>();
        }

        private static bool TrySplitCsvLine(string line, out string dateField, out string nameField)
        {
            dateField = string.Empty;
            nameField = string.Empty;

            var fields = new List<string>(3);
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (ch == ',' && !inQuotes)
                {
                    fields.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(ch);
                }
            }

            fields.Add(current.ToString().Trim());
            if (fields.Count < 2)
            {
                return false;
            }

            dateField = fields[0];
            nameField = fields[1];
            return true;
        }

        private static HolidayKind ClassifyHolidayKind(string name)
        {
            string trimmed = name.Trim();
            if (string.Equals(trimmed, "休日", StringComparison.Ordinal) ||
                string.Equals(trimmed, "振替休日", StringComparison.Ordinal))
            {
                return HolidayKind.Substitute;
            }

            if (string.Equals(trimmed, "国民の休日", StringComparison.Ordinal))
            {
                return HolidayKind.CitizensHoliday;
            }

            return HolidayKind.National;
        }
    }
}
