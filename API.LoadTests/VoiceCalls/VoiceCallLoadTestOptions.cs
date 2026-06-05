using System.Text.Json;
using System.Text.Json.Serialization;

namespace Messenger.LoadTests.VoiceCalls;

public sealed record VoiceCallLoadTestOptions
{
    [JsonPropertyName("calls")]
    public int Calls { get; init; } = 20;

    [JsonPropertyName("participantsPerCall")]
    public int ParticipantsPerCall { get; init; } = 6;

    [JsonPropertyName("durationSeconds")]
    public int DurationSeconds { get; init; } = 30;

    [JsonPropertyName("framesPerSecond")]
    public int FramesPerSecond { get; init; } = 50;

    [JsonPropertyName("rampUpSeconds")]
    public int RampUpSeconds { get; init; }

    [JsonPropertyName("warmUpSeconds")]
    public int WarmUpSeconds { get; init; } = 5;

    [JsonPropertyName("relayHost")]
    public string RelayHost { get; init; } = "127.0.0.1";

    [JsonPropertyName("relayPort")]
    public int RelayPort { get; init; }

    [JsonPropertyName("minReceiveRatio")]
    public double MinReceiveRatio { get; init; } = 0.50;

    [JsonPropertyName("verbose")]
    public bool Verbose { get; init; }

    [JsonIgnore]
    public bool ShowHelp { get; init; }

    [JsonIgnore]
    public bool UseAutoRelayPort => RelayPort == 0;

    [JsonIgnore]
    public int TotalParticipants => Calls * ParticipantsPerCall;

    [JsonIgnore]
    public TimeSpan Duration => TimeSpan.FromSeconds(DurationSeconds);

    [JsonIgnore]
    public TimeSpan RampUp => TimeSpan.FromSeconds(RampUpSeconds);

    [JsonIgnore]
    public TimeSpan WarmUp => TimeSpan.FromSeconds(WarmUpSeconds);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static VoiceCallLoadTestOptions FromFile(string path = "loadtest.json")
    {
        if (!File.Exists(path))
            return new VoiceCallLoadTestOptions();
        try
        {
            var json = File.ReadAllText(path);
            var options = JsonSerializer.Deserialize<VoiceCallLoadTestOptions>(json, JsonOptions);
            return options ?? new VoiceCallLoadTestOptions();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Ошибка разбора {path}: {ex.Message}", ex);
        }
    }

    public static VoiceCallLoadTestOptions Parse(string[] args, string jsonPath = "loadtest.json")
    {
        var options = FromFile(jsonPath);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg is "-h" or "--help")
                return options with { ShowHelp = true };

            if (arg is "--verbose")
            {
                options = options with { Verbose = true };
                continue;
            }

            if (!arg.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Неизвестный аргумент: {arg}");

            var key = arg[2..];
            var value = ReadValue(args, ref i, key);

            options = key switch
            {
                "calls" => options with { Calls = ParsePositiveInt(key, value) },
                "participants" => options with { ParticipantsPerCall = ParsePositiveInt(key, value) },
                "duration" => options with { DurationSeconds = ParsePositiveInt(key, value) },
                "fps" => options with { FramesPerSecond = ParsePositiveInt(key, value) },
                "ramp-up" => options with { RampUpSeconds = ParseNonNegativeInt(key, value) },
                "warm-up" => options with { WarmUpSeconds = ParseNonNegativeInt(key, value) },
                "relay-host" => options with { RelayHost = value },
                "relay-port" => options with { RelayPort = ParsePort(value) },
                "min-receive-ratio" => options with { MinReceiveRatio = ParseRatio(value) },
                "config" => FromFile(value),
                _ => throw new ArgumentException($"Неизвестная опция: --{key}")
            };
        }

        if (options.ParticipantsPerCall < 2)
            throw new ArgumentException(
                "--participants должен быть не меньше 2, чтобы звонок создавал микс для получателей.");

        return options;
    }

    public VoiceCallLoadTestOptions ResolveRelayPort(Func<int> portFactory)
        => UseAutoRelayPort ? this with { RelayPort = portFactory() } : this;

    private static string ReadValue(string[] args, ref int index, string key)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"Для --{key} требуется значение.");
        index++;
        return args[index];
    }

    private static int ParsePositiveInt(string key, string value)
    {
        if (int.TryParse(value, out var parsed) && parsed > 0)
            return parsed;
        throw new ArgumentException($"--{key} должен быть положительным целым числом.");
    }

    private static int ParseNonNegativeInt(string key, string value)
    {
        if (int.TryParse(value, out var parsed) && parsed >= 0)
            return parsed;
        throw new ArgumentException($"--{key} должен быть неотрицательным целым числом.");
    }

    private static int ParsePort(string value)
    {
        if (int.TryParse(value, out var parsed) && parsed is >= 0 and <= 65535)
            return parsed;
        throw new ArgumentException(
            "--relay-port должен быть от 0 до 65535, где 0 — автоматический выбор порта.");
    }

    private static double ParseRatio(string value)
    {
        if (double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            && parsed is >= 0 and <= 1)
            return parsed;
        throw new ArgumentException("--min-receive-ratio должен быть числом от 0 до 1.");
    }

    public const string HelpText = """
        Нагрузочный тест голосовых звонков.
        Базовые параметры читаются из loadtest.json (если файл существует).
        CLI-аргументы имеют приоритет над файлом.

        Пример:
          dotnet run --project API.LoadTests
          dotnet run --project API.LoadTests -- --calls 50 --participants 8 --duration 60
          dotnet run --project API.LoadTests -- --config heavy.json --duration 120

        Опции:
          --config <path>            Путь к JSON-файлу конфигурации. По умолчанию: loadtest.json
          --calls <n>                Количество одновременных групповых звонков. По умолчанию: 20
          --participants <n>         Количество участников в каждом звонке. По умолчанию: 6
          --duration <sec>           Длительность активной фазы теста. По умолчанию: 30
          --fps <n>                  Частота отправки Opus-фреймов каждым участником. По умолчанию: 50
          --ramp-up <sec>            Плавный запуск виртуальных участников. По умолчанию: 0
          --warm-up <sec>            Прогрев JIT/сокетов до начала измерений. По умолчанию: 3. Используйте 0 для отключения.
          --relay-host <host>        UDP-хост relay. По умолчанию: 127.0.0.1
          --relay-port <port>        UDP-порт relay. По умолчанию: 0 (автоматически)
          --min-receive-ratio <0..1> Минимальная доля полученных mixed-пакетов. По умолчанию: 0.50
          --verbose                  Включить подробные логи.
          --help                     Показать справку.
        """;
}