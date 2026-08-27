using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace YARG
{
    [DefaultExecutionOrder(-4999)]
    public static class CommandLineArgs
    {
        // Yes, the arguments should probably be prefixed with "--", however, this is based upon
        // Unity's existing command line arguments to make them consistent in style.

        /// <summary>
        /// Whether or not the game should be launched in offline mode. Offline mode disables
        /// online features such as fetching the OpenSource icons.
        /// </summary>
        private const string OFFLINE_ARG = "-offline";

        /// <summary>
        /// Defines whether we should save frame time data to replays
        /// </summary>
        private const string VERBOSE_REPLAYS = "-verbose-replays";

        /// <summary>
        /// Used to select the language the game will be launched in. The argument after should be
        /// the language code.
        /// </summary>
        private const string LANGUAGE_ARG = "-lang";

        /// <summary>
        /// Used to reference the download location of YARG and all of its setlists (by the launcher).
        /// The argument after should be the download location path.
        /// </summary>
        private const string DOWNLOAD_LOCATION_ARG = "-download-location";

        private const string PERSISTENT_DATA_PATH_ARG = "-persistent-data-path";
        private const string PERF_CSV_ARG = "-perf-csv";
        private const string PERF_WARMUP_ARG = "-perf-warmup";

        public static bool Offline { get; private set; }

        public static bool VerboseReplays { get; private set; }

        public static string Language           { get; private set; }
        public static string DownloadLocation   { get; private set; }
        public static string PersistentDataPath { get; private set; }
        public static string PerformanceCsvDirectory { get; private set; }
        public static float PerformanceWarmupSeconds { get; private set; } = 20f;
        public static string[] RawArguments { get; private set; } = Array.Empty<string>();

        private static bool _initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void InitCommandLineArgs()
        {
            Initialize();
        }

        public static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            var args = Environment.GetCommandLineArgs();
            RawArguments = args;

            // Remember, the first argument is always the application itself
            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case OFFLINE_ARG:
                        Offline = true;
                        break;
                    case VERBOSE_REPLAYS:
                        VerboseReplays = true;
                        break;
                    case LANGUAGE_ARG:
                        i++;
                        if (i < args.Length)
                        {
                            Language = args[i];
                        }

                        break;
                    case DOWNLOAD_LOCATION_ARG:
                        i++;
                        if (i < args.Length)
                        {
                            DownloadLocation = args[i];
                        }

                        break;
                    case PERSISTENT_DATA_PATH_ARG:
                        i++;
                        if (i < args.Length)
                        {
                            PersistentDataPath = args[i];
                        }

                        break;
                    case PERF_CSV_ARG:
                        i++;
                        if (i < args.Length)
                        {
                            PerformanceCsvDirectory = args[i];
                        }

                        break;
                    case PERF_WARMUP_ARG:
                        i++;
                        if (i < args.Length && float.TryParse(args[i], NumberStyles.Float, CultureInfo.InvariantCulture,
                                out float warmupSeconds))
                        {
                            PerformanceWarmupSeconds = Math.Max(0f, warmupSeconds);
                        }

                        break;
                }
            }
        }
    }
}