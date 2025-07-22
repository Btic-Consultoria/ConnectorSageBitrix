using ConnectorSageBitrix.Logging;

namespace ConnectorSageBitrix.Extensions
{
    /// <summary>
    /// Extension methods for Logger to add missing log levels
    /// </summary>
    public static class LoggerExtensions
    {
        /// <summary>
        /// Logs a warning message
        /// </summary>
        public static void Warning(this Logger logger, string message, params object[] args)
        {
            logger.Info($"[WARNING] {message}", args);
        }

        /// <summary>
        /// Logs a trace message
        /// </summary>
        public static void Trace(this Logger logger, string message, params object[] args)
        {
            logger.Debug($"[TRACE] {message}", args);
        }
    }
}