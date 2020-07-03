using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Serilog.Events;
using Serilog.Parsing;

namespace ModUpdater {
    public class HttpClientLoggingHandler : DelegatingHandler {
        private const string DefaultRequestCompletionMessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        private readonly MessageTemplate _messageTemplate;

        public HttpClientLoggingHandler() {
            _messageTemplate = new MessageTemplateParser().Parse(DefaultRequestCompletionMessageTemplate);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            var start = Stopwatch.GetTimestamp();
            try {
                var result = await base.SendAsync(request, cancellationToken);
                var elapsedMs = GetElapsedMilliseconds(start, Stopwatch.GetTimestamp());
                var statusCode = (int) result.StatusCode;
                LogCompletion(request, statusCode, elapsedMs, null);
                return result;
            } catch (Exception ex) when(LogCompletion(request, 500, GetElapsedMilliseconds(start, Stopwatch.GetTimestamp()), ex)) {
                throw;
            }
        }

        bool LogCompletion(HttpRequestMessage request, int statusCode, double elapsedMs, Exception ex) {
            var logger = Log.ForContext<HttpClientLoggingHandler>();

            LogEventLevel level = LogLevel(statusCode, ex);

            var properties = new [] {
                new LogEventProperty("RequestMethod", new ScalarValue(request.Method)),
                new LogEventProperty("RequestPath", new ScalarValue(request.RequestUri.PathAndQuery)),
                new LogEventProperty("StatusCode", new ScalarValue(statusCode)),
                new LogEventProperty("Elapsed", new ScalarValue(elapsedMs))
            };

            var evt = new LogEvent(DateTimeOffset.Now, level, ex, _messageTemplate, properties);
            logger.Write(evt);

            return false;
        }

        private static double GetElapsedMilliseconds(long start, long stop) {
            return (stop - start) * 1000 / (double) Stopwatch.Frequency;
        }

        private static LogEventLevel LogLevel(int statusCode, Exception ex) =>
            ex != null ? LogEventLevel.Error : statusCode > 499 ? LogEventLevel.Error : LogEventLevel.Debug;
    }
}