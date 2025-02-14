// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using Steeltoe.Common;
using Steeltoe.Common.Util;
using Steeltoe.Messaging;

namespace Steeltoe.Integration.Mapping;

public abstract class AbstractHeaderMapper<T> : IRequestReplyHeaderMapper<T>
{
    public const string StandardRequestHeaderNamePattern = "STANDARD_REQUEST_HEADERS";
    public const string StandardReplyHeaderNamePattern = "STANDARD_REPLY_HEADERS";
    public const string NonStandardHeaderNamePattern = "NON_STANDARD_HEADERS";

    private readonly List<string> _transientHeaderNames = new()
    {
        MessageHeaders.IdName,
        MessageHeaders.TimestampName
    };

    private readonly ILogger _logger;

    public string StandardHeaderPrefix { get; set; }

    public List<string> RequestHeaderNames { get; set; }

    public List<string> ReplyHeaderNames { get; set; }

    public IHeaderMatcher RequestHeaderMatcher { get; set; }

    public IHeaderMatcher ReplyHeaderMatcher { get; set; }

    protected AbstractHeaderMapper(string standardHeaderPrefix, List<string> requestHeaderNames, List<string> replyHeaderNames, ILogger logger)
    {
        StandardHeaderPrefix = standardHeaderPrefix;
        RequestHeaderNames = requestHeaderNames;
        ReplyHeaderNames = replyHeaderNames;
        _logger = logger;

#pragma warning disable S1699 // Constructors should only call non-overridable methods
        RequestHeaderMatcher = CreateDefaultHeaderMatcher(StandardHeaderPrefix, RequestHeaderNames);
        ReplyHeaderMatcher = CreateDefaultHeaderMatcher(StandardHeaderPrefix, ReplyHeaderNames);
#pragma warning restore S1699 // Constructors should only call non-overridable methods
    }

    public void SetRequestHeaderNames(params string[] requestHeaderNames)
    {
        ArgumentGuard.NotNull(requestHeaderNames);

        RequestHeaderMatcher = CreateHeaderMatcher(requestHeaderNames);
    }

    public void SetReplyHeaderNames(params string[] replyHeaderNames)
    {
        ArgumentGuard.NotNull(replyHeaderNames);

        ReplyHeaderMatcher = CreateHeaderMatcher(replyHeaderNames);
    }

    public void FromHeadersToRequest(IMessageHeaders headers, T target)
    {
        FromHeaders(headers, target, RequestHeaderMatcher);
    }

    public void FromHeadersToReply(IMessageHeaders headers, T target)
    {
        FromHeaders(headers, target, ReplyHeaderMatcher);
    }

    public IDictionary<string, object> ToHeadersFromRequest(T source)
    {
        return ToHeaders(source, RequestHeaderMatcher);
    }

    public IDictionary<string, object> ToHeadersFromReply(T source)
    {
        return ToHeaders(source, ReplyHeaderMatcher);
    }

    protected virtual IHeaderMatcher CreateDefaultHeaderMatcher(string standardHeaderPrefix, List<string> headerNames)
    {
        return new ContentBasedHeaderMatcher(true, new List<string>(headerNames));
    }

    protected virtual IHeaderMatcher CreateHeaderMatcher(string[] patterns)
    {
        var matchers = new List<IHeaderMatcher>();

        foreach (string pattern in patterns)
        {
            if (pattern == StandardRequestHeaderNamePattern)
            {
                matchers.Add(new ContentBasedHeaderMatcher(true, RequestHeaderNames));
            }
            else if (pattern == StandardReplyHeaderNamePattern)
            {
                matchers.Add(new ContentBasedHeaderMatcher(true, ReplyHeaderNames));
            }
            else if (pattern == NonStandardHeaderNamePattern)
            {
                matchers.Add(new PrefixBasedMatcher(false, StandardHeaderPrefix));
            }
            else
            {
                string thePattern = pattern;
                bool negate = false;

                if (pattern.StartsWith('!'))
                {
                    thePattern = pattern[1..];
                    negate = true;
                }
                else if (pattern.StartsWith("\\!", StringComparison.Ordinal))
                {
                    thePattern = pattern[1..];
                }

                if (negate)
                {
                    // negative matchers get priority
                    matchers.Insert(0, new SinglePatternBasedHeaderMatcher(thePattern, true));
                }
                else
                {
                    matchers.Add(new SinglePatternBasedHeaderMatcher(thePattern, false));
                }
            }
        }

        return new CompositeHeaderMatcher(matchers);
    }

    protected virtual TValue GetHeaderIfAvailable<TValue>(IDictionary<string, object> headers, string name, Type type)
    {
        headers.TryGetValue(name, out object value);

        if (value == null)
        {
            return default;
        }

        if (!type.IsInstanceOfType(value))
        {
            return default;
        }

        return (TValue)value;
    }

    protected virtual string CreateTargetPropertyName(string propertyName, bool fromMessageHeaders)
    {
        return propertyName;
    }

    protected virtual List<string> GetTransientHeaderNames()
    {
        return _transientHeaderNames;
    }

    protected abstract IDictionary<string, object> ExtractStandardHeaders(T source);

    protected abstract IDictionary<string, object> ExtractUserDefinedHeaders(T source);

    protected abstract void PopulateStandardHeaders(IDictionary<string, object> headers, T target);

    protected virtual void PopulateStandardHeaders(IDictionary<string, object> allHeaders, IDictionary<string, object> subset, T target)
    {
        PopulateStandardHeaders(subset, target);
    }

    protected abstract void PopulateUserDefinedHeader(string headerName, object headerValue, T target);

    private static bool IsMessageChannel(object headerValue)
    {
        return headerValue is IMessageChannel;
    }

    private void FromHeaders(IMessageHeaders headers, T target, IHeaderMatcher headerMatcher)
    {
        try
        {
            var subset = new Dictionary<string, object>();

            foreach (KeyValuePair<string, object> entry in (IDictionary<string, object>)headers)
            {
                string headerName = entry.Key;

                if (ShouldMapHeader(headerName, headerMatcher))
                {
                    subset[headerName] = entry.Value;
                }
            }

            PopulateStandardHeaders(headers, subset, target);
            PopulateUserDefinedHeaders(subset, target);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, ex.Message);
        }
    }

    private void PopulateUserDefinedHeaders(IDictionary<string, object> headers, T target)
    {
        foreach (KeyValuePair<string, object> entry in headers)
        {
            string headerName = entry.Key;
            object value = entry.Value;

            if (value != null && !IsMessageChannel(value))
            {
                try
                {
                    if (!headerName.StartsWith(StandardHeaderPrefix, StringComparison.Ordinal))
                    {
                        string key = CreateTargetPropertyName(headerName, true);
                        PopulateUserDefinedHeader(key, value, target);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, ex.Message);
                }
            }
        }
    }

    private Dictionary<string, object> ToHeaders(T source, IHeaderMatcher headerMatcher)
    {
        var headers = new Dictionary<string, object>();
        IDictionary<string, object> standardHeaders = ExtractStandardHeaders(source);
        CopyHeaders(standardHeaders, headers, headerMatcher);
        IDictionary<string, object> userDefinedHeaders = ExtractUserDefinedHeaders(source);
        CopyHeaders(userDefinedHeaders, headers, headerMatcher);
        return headers;
    }

    private void CopyHeaders(IDictionary<string, object> source, IDictionary<string, object> target, IHeaderMatcher headerMatcher)
    {
        if (source != null)
        {
            foreach (KeyValuePair<string, object> entry in source)
            {
                try
                {
                    string headerName = CreateTargetPropertyName(entry.Key, false);

                    if (ShouldMapHeader(headerName, headerMatcher))
                    {
                        target[headerName] = entry.Value;
                    }
                }
                catch (Exception)
                {
                    // Log
                }
            }
        }
    }

    private bool ShouldMapHeader(string headerName, IHeaderMatcher headerMatcher)
    {
        return !(string.IsNullOrEmpty(headerName) || GetTransientHeaderNames().Contains(headerName)) && headerMatcher.MatchHeader(headerName);
    }

    public interface IHeaderMatcher
    {
        bool IsNegated { get; }

        bool MatchHeader(string headerName);
    }

    protected class ContentBasedHeaderMatcher : IHeaderMatcher
    {
        private bool Match { get; }

        private List<string> Content { get; }

        public bool IsNegated => false;

        public ContentBasedHeaderMatcher(bool match, List<string> content)
        {
            ArgumentGuard.NotNull(content);

            Match = match;
            Content = content;
        }

        public bool MatchHeader(string headerName)
        {
            bool result = Match == ContainsIgnoreCase(headerName);
            return result;
        }

        private bool ContainsIgnoreCase(string name)
        {
            foreach (string headerName in Content)
            {
                if (headerName.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    protected class PatternBasedHeaderMatcher : IHeaderMatcher
    {
        private List<string> Patterns { get; } = new();

        public bool IsNegated => false;

        public PatternBasedHeaderMatcher(List<string> patterns)
        {
            ArgumentGuard.NotNullOrEmpty(patterns);

            foreach (string pattern in patterns)
            {
#pragma warning disable S4040 // Strings should be normalized to uppercase
                Patterns.Add(pattern.ToLowerInvariant());
#pragma warning restore S4040 // Strings should be normalized to uppercase
            }
        }

        public bool MatchHeader(string headerName)
        {
#pragma warning disable S4040 // Strings should be normalized to uppercase
            string header = headerName.ToLowerInvariant();
#pragma warning restore S4040 // Strings should be normalized to uppercase

            foreach (string pattern in Patterns)
            {
                if (PatternMatchUtils.SimpleMatch(pattern, header))
                {
                    return true;
                }
            }

            return false;
        }
    }

    protected class SinglePatternBasedHeaderMatcher : IHeaderMatcher
    {
        private string Pattern { get; }

        private bool Negate { get; }

        public bool IsNegated => Negate;

        public SinglePatternBasedHeaderMatcher(string pattern)
            : this(pattern, false)
        {
        }

        public SinglePatternBasedHeaderMatcher(string pattern, bool negate)
        {
            ArgumentGuard.NotNull(pattern);

#pragma warning disable S4040 // Strings should be normalized to uppercase
            Pattern = pattern.ToLowerInvariant();
#pragma warning restore S4040 // Strings should be normalized to uppercase

            Negate = negate;
        }

        public bool MatchHeader(string headerName)
        {
#pragma warning disable S4040 // Strings should be normalized to uppercase
            string header = headerName.ToLowerInvariant();
#pragma warning restore S4040 // Strings should be normalized to uppercase

            if (PatternMatchUtils.SimpleMatch(Pattern, header))
            {
                return true;
            }

            return false;
        }
    }

    protected class PrefixBasedMatcher : IHeaderMatcher
    {
        private bool Match { get; }

        private string Prefix { get; }

        public bool IsNegated { get; }

        public PrefixBasedMatcher(bool match, string prefix)
        {
            Match = match;
            Prefix = prefix;
        }

        public bool MatchHeader(string headerName)
        {
            bool result = Match == headerName.StartsWith(Prefix, StringComparison.Ordinal);
            return result;
        }
    }

    protected class CompositeHeaderMatcher : IHeaderMatcher
    {
        private List<IHeaderMatcher> Matchers { get; }

        public bool IsNegated { get; }

        public CompositeHeaderMatcher(List<IHeaderMatcher> strategies)
        {
            Matchers = strategies;
        }

        public CompositeHeaderMatcher(params IHeaderMatcher[] strategies)
            : this(new List<IHeaderMatcher>(strategies))
        {
        }

        public bool MatchHeader(string headerName)
        {
            foreach (IHeaderMatcher strategy in Matchers)
            {
                if (strategy.MatchHeader(headerName))
                {
                    if (strategy.IsNegated)
                    {
                        break;
                    }

                    return true;
                }
            }

            return false;
        }
    }
}
