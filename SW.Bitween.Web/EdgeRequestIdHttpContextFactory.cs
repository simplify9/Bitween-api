using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace SW.Bitween.Web
{
    /// <summary>
    /// Adopts the ingress's X-Request-ID as the request's TraceIdentifier, which is what the
    /// logging pipeline reports as RequestId.
    /// <para>
    /// This has to happen in the context factory rather than in middleware: ASP.NET opens the
    /// logging scope that captures RequestId immediately after the context is created and before
    /// the middleware pipeline runs, so a middleware assignment lands too late and every log line
    /// still carries ASP.NET's own id. That id joins to nothing at the ingress, which is what makes
    /// pivoting from a failing HTTP request to this service's logs impossible.
    /// </para>
    /// </summary>
    public class EdgeRequestIdHttpContextFactory : IHttpContextFactory
    {
        private const string EdgeRequestIdHeader = "X-Request-ID";

        private readonly IHttpContextFactory inner;

        public EdgeRequestIdHttpContextFactory(IHttpContextFactory inner)
        {
            this.inner = inner;
        }

        public HttpContext Create(IFeatureCollection featureCollection)
        {
            var context = inner.Create(featureCollection);

            var edgeId = context.Request.Headers[EdgeRequestIdHeader]
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (edgeId != null) context.TraceIdentifier = edgeId;

            return context;
        }

        public void Dispose(HttpContext httpContext) => inner.Dispose(httpContext);
    }
}
