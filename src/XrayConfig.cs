using System.Text.Json;
using System.Text.Json.Nodes;

namespace SiberiaConnect;

/// <summary>Сборка конфига Xray из ссылки vless:// нашей подписки.</summary>
internal static class XrayConfig
{
    // Российские сайты — напрямую (как в Android-приложении)
    private static readonly string[] RuDomains =
    {
        "domain:ru", "domain:su", "domain:xn--p1ai", "domain:xn--80adxhks", "domain:xn--d1acj3b",
        "domain:yandex.net", "domain:yandex.com", "domain:yastatic.net", "domain:yandexcloud.net", "domain:yandex.cloud",
        "domain:vk.com", "domain:vk.me", "domain:vkuser.net", "domain:userapi.com", "domain:vk-cdn.net", "domain:vk-portal.net", "domain:vkvideo.ru",
        "domain:mycdn.me", "domain:okcdn.ru", "domain:mail.ru", "domain:imgsmail.ru", "domain:mradx.net",
        "domain:2gis.com", "domain:avito.st", "domain:wbstatic.net", "domain:wbbasket.ru", "domain:ozone.ru", "domain:ozonusercontent.com",
        "domain:sberbank.com", "domain:sber.ru", "domain:sbrf.ru", "domain:tbank-online.com", "domain:tinkoffjournal.ru",
        "domain:kaspersky.com", "domain:drweb.com", "domain:1c.ru", "domain:mos.ru", "domain:nalog.gov.ru", "domain:gosuslugi.ru",
        "domain:rutube.ru", "domain:dzen.ru", "domain:kinopoisk.ru", "domain:ivi.ru", "domain:okko.tv", "domain:start.ru", "domain:wink.ru",
        "domain:max.ru", "domain:oneme.ru", "domain:rustore.ru", "domain:hh.ru", "domain:rzd.ru", "domain:tutu.ru"
    };

    public static string Build(string link, int socksPort, int httpPort, bool useGeo, bool tun = false)
    {
        var proxy = Outbound(link);
        var sniff = new JsonObject
        {
            ["enabled"] = true,
            ["destOverride"] = new JsonArray("http", "tls"),
            ["routeOnly"] = true
        };

        var rules = new JsonArray();
        if (useGeo)
        {
            rules.Add(Rule("direct", ip: new[] { "geoip:private" }));
            rules.Add(Rule("direct", domain: new[] { "geosite:private" }));
            rules.Add(Rule("direct", domain: RuDomains.Append("geosite:category-ru").ToArray()));
            rules.Add(Rule("direct", ip: new[] { "geoip:ru" }));
        }
        else
        {
            rules.Add(Rule("direct", domain: new[] { "localhost" }));
            rules.Add(Rule("direct", domain: RuDomains));
        }
        rules.Add(new JsonObject { ["type"] = "field", ["network"] = "tcp,udp", ["outboundTag"] = "proxy" });

        var cfg = new JsonObject
        {
            ["log"] = new JsonObject { ["loglevel"] = "warning", ["access"] = "none" },
            ["inbounds"] = new JsonArray(
                new JsonObject
                {
                    ["tag"] = "socks", ["listen"] = "127.0.0.1", ["port"] = socksPort, ["protocol"] = "socks",
                    ["settings"] = new JsonObject { ["auth"] = "noauth", ["udp"] = true },
                    ["sniffing"] = sniff.DeepClone()
                },
                new JsonObject
                {
                    ["tag"] = "http", ["listen"] = "127.0.0.1", ["port"] = httpPort, ["protocol"] = "http",
                    ["settings"] = new JsonObject(),
                    ["sniffing"] = sniff.DeepClone()
                }),
            ["outbounds"] = new JsonArray(
                proxy,
                new JsonObject { ["tag"] = "direct", ["protocol"] = "freedom" },
                new JsonObject { ["tag"] = "block", ["protocol"] = "blackhole" }),
            ["routing"] = new JsonObject
            {
                // в режиме «Весь компьютер» решаем по имени сайта, без DNS-запросов к провайдеру (он может вернуть замедленный российский кэш Google)
                ["domainStrategy"] = useGeo && !tun ? "IPIfNonMatch" : "AsIs",
                ["rules"] = rules
            }
        };
        return cfg.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>1.2.0: конфиг для проверки «Авто» — по HTTP-входу на каждый сервер (вход i → сервер i).</summary>
    public static string BuildProbe(string[] links, int[] ports, out bool[] valid)
    {
        valid = new bool[links.Length];
        var inbounds = new JsonArray();
        var outbounds = new JsonArray();
        var rules = new JsonArray();
        for (var i = 0; i < links.Length; i++)
        {
            JsonObject ob;
            try { ob = Outbound(links[i]); }
            catch { continue; }
            ob["tag"] = "p" + i;
            outbounds.Add(ob);
            inbounds.Add(new JsonObject
            {
                ["tag"] = "in" + i, ["listen"] = "127.0.0.1", ["port"] = ports[i], ["protocol"] = "http",
                ["settings"] = new JsonObject()
            });
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["inboundTag"] = new JsonArray((JsonNode)JsonValue.Create("in" + i)),
                ["outboundTag"] = "p" + i
            });
            valid[i] = true;
        }
        outbounds.Add(new JsonObject { ["tag"] = "block", ["protocol"] = "blackhole" });
        return new JsonObject
        {
            ["log"] = new JsonObject { ["loglevel"] = "warning", ["access"] = "none" },
            ["inbounds"] = inbounds,
            ["outbounds"] = outbounds,
            ["routing"] = new JsonObject { ["domainStrategy"] = "AsIs", ["rules"] = rules }
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject Rule(string tag, string[] domain = null, string[] ip = null)
    {
        var r = new JsonObject { ["type"] = "field", ["outboundTag"] = tag };
        if (domain != null) r["domain"] = new JsonArray(domain.Select(d => (JsonNode)JsonValue.Create(d)).ToArray());
        if (ip != null) r["ip"] = new JsonArray(ip.Select(d => (JsonNode)JsonValue.Create(d)).ToArray());
        return r;
    }

    private static JsonObject Outbound(string link)
    {
        if (!link.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("поддерживаются только серверы VLESS");
        var main = link;
        var hash = main.IndexOf('#');
        if (hash >= 0) main = main[..hash];
        var rest = main["vless://".Length..];
        var at = rest.LastIndexOf('@');
        if (at <= 0) throw new FormatException("нет идентификатора");
        var id = Uri.UnescapeDataString(rest[..at]);
        rest = rest[(at + 1)..];
        var query = "";
        var qi = rest.IndexOf('?');
        if (qi >= 0) { query = rest[(qi + 1)..]; rest = rest[..qi]; }
        rest = rest.TrimEnd('/');

        string host;
        int port;
        if (rest.StartsWith('['))
        {
            var rb = rest.IndexOf(']');
            host = rest[1..rb];
            port = int.Parse(rest[(rb + 2)..]);
        }
        else
        {
            var ci = rest.LastIndexOf(':');
            host = rest[..ci];
            port = int.Parse(rest[(ci + 1)..]);
        }

        var q = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var k = Uri.UnescapeDataString(eq >= 0 ? part[..eq] : part);
            var v = eq >= 0 ? Uri.UnescapeDataString(part[(eq + 1)..]) : "";
            q[k] = v;
        }
        string G(string k, string def = "") => q.TryGetValue(k, out var v) && !string.IsNullOrEmpty(v) ? v : def;

        var net = G("type", "tcp").ToLowerInvariant();
        var sec = G("security", "none").ToLowerInvariant();
        var stream = new JsonObject { ["network"] = net, ["security"] = sec };
        // 1.4.1: TCP keep-alive со стороны клиента (как на серверах): NAT роутера или оператора и ТСПУ не закрывают простаивающие соединения по таймауту
        stream["sockopt"] = new JsonObject { ["tcpKeepAliveIdle"] = 30, ["tcpKeepAliveInterval"] = 15 };

        if (sec == "reality")
        {
            stream["realitySettings"] = new JsonObject
            {
                ["serverName"] = G("sni"),
                ["fingerprint"] = G("fp", "chrome"),
                ["publicKey"] = G("pbk"),
                ["shortId"] = G("sid"),
                ["spiderX"] = G("spx")
            };
        }
        else if (sec == "tls")
        {
            var t = new JsonObject { ["serverName"] = G("sni", host), ["fingerprint"] = G("fp", "chrome") };
            var alpn = G("alpn");
            if (alpn != "") t["alpn"] = new JsonArray(alpn.Split(',').Select(a => (JsonNode)JsonValue.Create(a.Trim())).ToArray());
            stream["tlsSettings"] = t;
        }

        switch (net)
        {
            case "xhttp":
            case "splithttp":
            {
                var x = new JsonObject { ["path"] = G("path", "/"), ["mode"] = G("mode", "auto") };
                var h = G("host");
                if (h != "") x["host"] = h;
                var extra = G("extra");
                if (extra != "") { try { x["extra"] = JsonNode.Parse(extra); } catch { } }
                stream["network"] = "xhttp";
                stream["xhttpSettings"] = x;
                break;
            }
            case "grpc":
                stream["grpcSettings"] = new JsonObject
                {
                    ["serviceName"] = G("serviceName"),
                    ["multiMode"] = G("mode") == "multi"
                };
                break;
            case "ws":
            {
                var w = new JsonObject { ["path"] = G("path", "/") };
                var h = G("host");
                if (h != "") w["host"] = h;
                stream["wsSettings"] = w;
                break;
            }
            case "httpupgrade":
            {
                var w = new JsonObject { ["path"] = G("path", "/") };
                var h = G("host");
                if (h != "") w["host"] = h;
                stream["httpupgradeSettings"] = w;
                break;
            }
        }

        var user = new JsonObject { ["id"] = id, ["encryption"] = G("encryption", "none") };
        var flow = G("flow");
        if (flow != "" && (net == "tcp" || net == "raw")) user["flow"] = flow;

        return new JsonObject
        {
            ["tag"] = "proxy",
            ["protocol"] = "vless",
            ["settings"] = new JsonObject
            {
                ["vnext"] = new JsonArray(new JsonObject
                {
                    ["address"] = host,
                    ["port"] = port,
                    ["users"] = new JsonArray(user)
                })
            },
            ["streamSettings"] = stream
        };
    }
}
