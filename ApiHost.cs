using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

// This entire file is NIHing a REST server because pulling in libraries is effort.
// Also it was fun to write.
// Just slap this thing behind an Nginx reverse proxy. It's not supposed to be directly exposed to the web.
namespace SS14.Watchdog
{
    internal sealed class ApiHost : IDisposable
    {
        private readonly Config _config;
        private readonly Program _program;

        // See this SO post for inspiration: https://stackoverflow.com/a/4673210

        private HttpListener _listener;
        private Thread _listenerThread;
        private ManualResetEventSlim _stop;

        public ApiHost(Config config, Program program)
        {
            _config = config;
            _program = program;
        }

        public void Start()
        {
            _stop = new ManualResetEventSlim();
            _listener = new HttpListener();
            _listener.Prefixes.Add(_config.ApiBind);
            _listener.Start();
            _listenerThread = new Thread(_worker)
            {
                Name = "REST API Thread",
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal
            };
            _listenerThread.Start();
        }

        public void Dispose()
        {
            if (_stop == null)
            {
                return;
            }

            _stop.Set();
            _listenerThread.Join(1000);
            _listener.Stop();
        }

        private void _worker()
        {
            while (_listener.IsListening)
            {
                var context = _listener.BeginGetContext(ar =>
                {
                    var actualContext = _listener.EndGetContext(ar);
                    _processRequest(actualContext);
                }, null);

                if (0 == WaitHandle.WaitAny(new[] {_stop.WaitHandle, context.AsyncWaitHandle}))
                {
                    return;
                }
            }
        }

        private void _processRequest(HttpListenerContext context)
        {
            _processRequestInternal(context);
            Console.WriteLine("[API] {0} -> {1} {2}", context.Request.Url.AbsolutePath,
                context.Response.StatusCode,
                context.Response.StatusDescription);
        }

        private void _processRequestInternal(HttpListenerContext context)
        {
            var response = context.Response;
            var request = context.Request;

            try
            {
                if (request.HttpMethod == "POST")
                {
                    _handlePost(context);
                }
                else if (request.HttpMethod == "GET" || request.HttpMethod == "HEAD")
                {
                    _handleGet(context);
                }
                else
                {
                    response.StatusCode = (int) HttpStatusCode.BadRequest;
                    response.StatusDescription = "Bad Request";
                    response.ContentType = "text/plain";
                    _respondText(response, "400 Bad Request", false);
                }
            }
            catch (Exception e)
            {
                response.StatusCode = (int) HttpStatusCode.InternalServerError;
                response.StatusDescription = "Internal Server Error";
                response.ContentType = "text/plain";
                _respondText(response, "500 Internal Server Error", false);
                Console.WriteLine("[API] exception while handling request: {0}", e);
            }
        }

        private static void _respondText(HttpListenerResponse response, string contents, bool head)
        {
            response.ContentEncoding = Encoding.UTF8;
            if (head)
            {
                response.OutputStream.Close();
                return;
            }

            using (var writer = new StreamWriter(response.OutputStream, Encoding.UTF8))
            {
                writer.Write(contents);
            }

            response.OutputStream.Close();
        }

        private void _handleGet(HttpListenerContext context)
        {
            var response = context.Response;
            var request = context.Request;

            var head = request.HttpMethod == "HEAD";
            var uri = request.Url;
            if (uri.AbsolutePath == "/teapot")
            {
                response.StatusCode = 418; // >HttpStatusCode doesn't include 418.
                response.StatusDescription = "I'm a teapot";
                response.ContentType = "text/plain";
                _respondText(response, "The requested entity body is short and stout.", head);
            }
            else if (uri.AbsolutePath == "/status")
            {

            }
            else
            {
                _return404(context, head);
            }
        }

        private void _handlePost(HttpListenerContext context)
        {
            var response = context.Response;
            var request = context.Request;

            var password = request.Headers["X-SS14W-Pass"];
            if (password == null)
            {
                _return400(context);
                return;
            }

            if (password != _config.Password)
            {
                _return400(context);
                return;
            }

            if (request.Url.AbsolutePath == "/update")
            {
                _program.RunUpdate();

                response.StatusCode = (int) HttpStatusCode.OK;
                response.StatusDescription = "OK";
                response.OutputStream.Close();
            }
            else if (request.Url.AbsolutePath == "/restart")
            {
                _program.Restart();

                response.StatusCode = (int) HttpStatusCode.OK;
                response.StatusDescription = "OK";
                response.OutputStream.Close();
            }
            else
            {
                _return404(context, false);
            }
        }

        private void _return404(HttpListenerContext context, bool head)
        {
            var response = context.Response;

            response.StatusCode = (int) HttpStatusCode.NotFound;
            response.StatusDescription = "Not Found";
            response.ContentType = "text/plain";
            _respondText(response, "404 Not Found", head);
        }

        private void _return400(HttpListenerContext context)
        {
            var response = context.Response;

            response.StatusCode = (int) HttpStatusCode.BadRequest;
            response.StatusDescription = "Bad Request";
            response.ContentType = "text/plain";
            _respondText(response, "400 Bad Request", false);
        }
    }
}