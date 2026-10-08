using System;
using System.Collections.Generic;
using System.IO;

namespace HyperTizen.Core
{
    // Maps URL paths to files inside one folder, and never to anything outside it.
    public static class StaticFiles
    {
        private static readonly Dictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".html", "text/html" },
            { ".css", "text/css" },
            { ".js", "text/javascript" },
            { ".json", "application/json" },
            { ".png", "image/png" },
            { ".svg", "image/svg+xml" }
        };

        // The file a URL path names inside the folder, or null when there is none.
        public static string Resolve(string folder, string urlPath)
        {
            string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string relative = (urlPath ?? string.Empty).TrimStart('/');
            if (relative.Length == 0) relative = "index.html";

            string full;
            try
            {
                full = Path.GetFullPath(Path.Combine(root, relative));
            }
            catch (Exception)
            {
                return null;
            }

            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return null;
            return File.Exists(full) ? full : null;
        }

        public static string ContentTypeFor(string file)
        {
            string contentType;
            return ContentTypes.TryGetValue(Path.GetExtension(file), out contentType) ? contentType : "application/octet-stream";
        }
    }
}
