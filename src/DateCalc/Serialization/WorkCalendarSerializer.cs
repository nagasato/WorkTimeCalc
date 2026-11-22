using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using DateCalc.Holidays;

namespace DateCalc.Serialization
{
    /// <summary>
    /// <see cref="WorkCalendarDefinition"/> および <see cref="WorkCalendar"/> / <see cref="WorkCalendarRegistry"/> の
    /// JSON シリアライズ・デシリアライズを行うヘルパークラスです。
    /// </summary>
    public static class WorkCalendarSerializer
    {
        /// <summary>
        /// DateCalc 向けの標準 <see cref="JsonSerializerOptions"/> を生成します。
        /// </summary>
        /// <param name="writeIndented">整形出力を行う場合は <c>true</c>。</param>
        /// <returns>構成済みの <see cref="JsonSerializerOptions"/>。</returns>
        public static JsonSerializerOptions CreateDefaultOptions(bool writeIndented = true)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                WriteIndented = writeIndented,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        /// <summary>
        /// カレンダー定義を JSON 文字列へシリアライズします。
        /// </summary>
        /// <param name="definition">シリアライズ対象のカレンダー定義。</param>
        /// <param name="options">任意のシリアライズオプション。</param>
        /// <returns>JSON 文字列。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="definition"/> が <c>null</c> の場合にスローされます。</exception>
        public static string Serialize(WorkCalendarDefinition definition, JsonSerializerOptions? options = null)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            return JsonSerializer.Serialize(definition, options ?? CreateDefaultOptions());
        }

        /// <summary>
        /// カレンダーインスタンスを DTO 経由で JSON 文字列へシリアライズします。
        /// </summary>
        /// <param name="calendar">シリアライズ対象のカレンダー。</param>
        /// <param name="options">任意のシリアライズオプション。</param>
        /// <returns>JSON 文字列。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="calendar"/> が <c>null</c> の場合にスローされます。</exception>
        public static string Serialize(IWorkCalendar calendar, JsonSerializerOptions? options = null)
        {
            if (calendar == null)
            {
                throw new ArgumentNullException(nameof(calendar));
            }

            return Serialize(calendar.ToDefinition(), options);
        }

        /// <summary>
        /// 複数のカレンダー定義を JSON 配列文字列へシリアライズします。
        /// </summary>
        /// <param name="definitions">カレンダー定義一覧。</param>
        /// <param name="options">任意のシリアライズオプション。</param>
        /// <returns>JSON 配列文字列。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="definitions"/> が <c>null</c> の場合にスローされます。</exception>
        public static string SerializeMany(
            IEnumerable<WorkCalendarDefinition> definitions,
            JsonSerializerOptions? options = null)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            return JsonSerializer.Serialize(definitions, options ?? CreateDefaultOptions());
        }

        /// <summary>
        /// 複数のカレンダーインスタンスを JSON 配列文字列へシリアライズします。
        /// </summary>
        /// <param name="calendars">カレンダー一覧。</param>
        /// <param name="options">任意のシリアライズオプション。</param>
        /// <returns>JSON 配列文字列。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="calendars"/> が <c>null</c> の場合にスローされます。</exception>
        public static string SerializeMany(
            IEnumerable<IWorkCalendar> calendars,
            JsonSerializerOptions? options = null)
        {
            if (calendars == null)
            {
                throw new ArgumentNullException(nameof(calendars));
            }

            var definitions = new List<WorkCalendarDefinition>();
            foreach (IWorkCalendar calendar in calendars)
            {
                if (calendar == null)
                {
                    throw new ArgumentException("カレンダー一覧に null を含めることはできません。", nameof(calendars));
                }

                definitions.Add(calendar.ToDefinition());
            }

            return SerializeMany(definitions, options);
        }

        /// <summary>
        /// JSON 文字列を単一の <see cref="WorkCalendarDefinition"/> へデシリアライズします。
        /// </summary>
        /// <param name="json">JSON 文字列。</param>
        /// <param name="options">任意のシリアライズオプション。</param>
        /// <returns>デシリアライズされた <see cref="WorkCalendarDefinition"/>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="json"/> が <c>null</c> の場合にスローされます。</exception>
        /// <exception cref="JsonException">JSON の解析に失敗した場合、または結果が <c>null</c> の場合にスローされます。</exception>
        public static WorkCalendarDefinition Deserialize(string json, JsonSerializerOptions? options = null)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            WorkCalendarDefinition? definition =
                JsonSerializer.Deserialize<WorkCalendarDefinition>(json, options ?? CreateDefaultOptions());

            return definition ?? throw new JsonException("JSON から WorkCalendarDefinition をデシリアライズできませんでした。");
        }

        /// <summary>
        /// JSON 配列文字列を複数の <see cref="WorkCalendarDefinition"/> 一覧へデシリアライズします。
        /// </summary>
        /// <param name="json">JSON 配列文字列。</param>
        /// <param name="options">任意のシリアライズオプション。</param>
        /// <returns>デシリアライズされた定義一覧。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="json"/> が <c>null</c> の場合にスローされます。</exception>
        /// <exception cref="JsonException">JSON の解析に失敗した場合、または結果が <c>null</c> の場合にスローされます。</exception>
        public static IReadOnlyList<WorkCalendarDefinition> DeserializeMany(string json, JsonSerializerOptions? options = null)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            List<WorkCalendarDefinition>? definitions =
                JsonSerializer.Deserialize<List<WorkCalendarDefinition>>(json, options ?? CreateDefaultOptions());

            return definitions ?? throw new JsonException("JSON からカレンダー定義配列をデシリアライズできませんでした。");
        }

        /// <summary>
        /// JSON 文字列から直接 <see cref="WorkCalendar"/> を構築します。
        /// </summary>
        /// <param name="json">単一カレンダー定義を表す JSON 文字列。</param>
        /// <param name="holidayProviderOverride">祝日プロバイダを外部から差し替える場合に指定します（省略可）。</param>
        /// <param name="options">任意のシリアライズオプション。</param>
        /// <returns>構築された <see cref="WorkCalendar"/>。</returns>
        public static WorkCalendar LoadCalendarFromJson(
            string json,
            IHolidayProvider? holidayProviderOverride = null,
            JsonSerializerOptions? options = null)
        {
            WorkCalendarDefinition definition = Deserialize(json, options);
            return WorkCalendar.FromDefinition(definition, holidayProviderOverride);
        }

        /// <summary>
        /// JSON 配列文字列から直接 <see cref="WorkCalendarRegistry"/> を構築します。
        /// </summary>
        /// <param name="json">複数カレンダー定義の JSON 配列文字列。</param>
        /// <param name="defaultHolidayProvider">各カレンダーの祝日プロバイダを外部指定（内閣府 CSV 等）に差し替える場合に指定します（省略可）。</param>
        /// <param name="options">任意のシリアライズオプション。</param>
        /// <returns>構築された <see cref="WorkCalendarRegistry"/>。</returns>
        public static WorkCalendarRegistry LoadRegistryFromJson(
            string json,
            IHolidayProvider? defaultHolidayProvider = null,
            JsonSerializerOptions? options = null)
        {
            IReadOnlyList<WorkCalendarDefinition> definitions = DeserializeMany(json, options);
            return WorkCalendarRegistry.FromDefinitions(definitions, defaultHolidayProvider);
        }
    }
}
