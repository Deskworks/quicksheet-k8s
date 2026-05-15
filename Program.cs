using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// QuickSheet Kubernetes Extension.
/// Prefix: "k8s". Usage: "k8s: namespace" or "k8s: all"
/// Shows pod status from current kubeconfig context via kubectl.
/// </summary>
class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                string? type = doc.RootElement.TryGetProperty("type", out var tp) ? tp.GetString() : null;

                switch (type)
                {
                    case "init":
                        HandleInit();
                        break;
                    case "activate":
                        HandleActivate(doc.RootElement);
                        break;
                    case "deactivate":
                        break;
                }
            }
            catch (Exception ex)
            {
                SendJson(new { type = "error", id = "", message = $"Parse error: {ex.Message}" });
            }
        }
    }

    static void HandleInit()
    {
        SendJson(new
        {
            type = "register",
            prefix = "k8s",
            name = "Kubernetes Pod Status",
            version = "1.0.0"
        });
        SendLog("Kubernetes extension registered. Uses kubectl from current kubeconfig.");
    }

    static void HandleActivate(JsonElement root)
    {
        string id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";

        string[] extParams = Array.Empty<string>();
        if (root.TryGetProperty("params", out var paramsProp) && paramsProp.ValueKind == JsonValueKind.Array)
        {
            extParams = paramsProp.EnumerateArray()
                .Select(p => p.GetString()?.Trim() ?? "")
                .Where(p => p.Length > 0)
                .ToArray();
        }

        string ns = extParams.Length > 0 ? extParams[0] : "default";
        bool allNamespaces = ns.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                             ns.Equals("*", StringComparison.OrdinalIgnoreCase);

        // Try to get current context first
        string context = RunKubectl("config current-context").Trim();
        if (string.IsNullOrEmpty(context) || context.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
        {
            SendCells(id, new List<CellData>
            {
                new(0, 0, "⚠️ kubectl not found or no context set"),
                new(1, 0, "Install: https://kubernetes.io/docs/tasks/tools/"),
                new(2, 0, "Set context: kubectl config use-context <name>")
            });
            return;
        }

        // Get pods
        string nsFlag = allNamespaces ? "--all-namespaces" : $"-n {ns}";
        string columns = allNamespaces
            ? "NAMESPACE:.metadata.namespace,NAME:.metadata.name,STATUS:.status.phase,RESTARTS:.status.containerStatuses[0].restartCount,AGE:.metadata.creationTimestamp"
            : "NAME:.metadata.name,STATUS:.status.phase,READY:.status.conditions[?(@.type==\"Ready\")].status,RESTARTS:.status.containerStatuses[0].restartCount,AGE:.metadata.creationTimestamp";

        string output = RunKubectl($"get pods {nsFlag} --no-headers -o custom-columns={columns}");

        if (string.IsNullOrWhiteSpace(output))
        {
            SendCells(id, new List<CellData>
            {
                new(0, 0, $"☸ Context: {context}"),
                new(1, 0, $"📭 No pods found in {(allNamespaces ? "any namespace" : $"namespace '{ns}'")}"),
            });
            return;
        }

        var cells = new List<CellData>();
        int row = 0;

        // Context header
        cells.Add(new CellData(row, 0, $"☸ {context}"));
        cells.Add(new CellData(row, 1, allNamespaces ? "all namespaces" : $"ns: {ns}"));
        row++;

        // Column headers
        if (allNamespaces)
        {
            cells.Add(new CellData(row, 0, "NAMESPACE"));
            cells.Add(new CellData(row, 1, "POD"));
            cells.Add(new CellData(row, 2, "STATUS"));
            cells.Add(new CellData(row, 3, "RESTARTS"));
            cells.Add(new CellData(row, 4, "AGE"));
        }
        else
        {
            cells.Add(new CellData(row, 0, "POD"));
            cells.Add(new CellData(row, 1, "STATUS"));
            cells.Add(new CellData(row, 2, "READY"));
            cells.Add(new CellData(row, 3, "RESTARTS"));
            cells.Add(new CellData(row, 4, "AGE"));
        }
        row++;

        // Parse pod lines
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        int maxRows = 30; // Cap to avoid flooding the grid
        foreach (var podLine in lines.Take(maxRows))
        {
            var parts = podLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) continue;

            if (allNamespaces && parts.Length >= 5)
            {
                cells.Add(new CellData(row, 0, parts[0]));                          // namespace
                cells.Add(new CellData(row, 1, TruncateName(parts[1], 40)));        // pod name
                cells.Add(new CellData(row, 2, StatusIcon(parts[2]) + " " + parts[2])); // status
                cells.Add(new CellData(row, 3, parts[3] == "<none>" ? "0" : parts[3])); // restarts
                cells.Add(new CellData(row, 4, FormatAge(parts[4])));               // age
            }
            else if (!allNamespaces && parts.Length >= 4)
            {
                cells.Add(new CellData(row, 0, TruncateName(parts[0], 45)));        // pod name
                cells.Add(new CellData(row, 1, StatusIcon(parts[1]) + " " + parts[1])); // status
                cells.Add(new CellData(row, 2, parts[2] == "<none>" ? "—" : parts[2])); // ready
                cells.Add(new CellData(row, 3, parts[3] == "<none>" ? "0" : parts[3])); // restarts
                if (parts.Length >= 5)
                    cells.Add(new CellData(row, 4, FormatAge(parts[4])));           // age
            }
            row++;
        }

        if (lines.Length > maxRows)
        {
            cells.Add(new CellData(row, 0, $"… +{lines.Length - maxRows} more pods"));
        }

        SendCells(id, cells);
    }

    static string StatusIcon(string status) => status.ToLowerInvariant() switch
    {
        "running" => "🟢",
        "succeeded" => "✅",
        "completed" => "✅",
        "pending" => "🟡",
        "containercreating" => "🟡",
        "imagepullbackoff" => "🔴",
        "crashloopbackoff" => "🔴",
        "error" => "🔴",
        "failed" => "🔴",
        "terminating" => "🟠",
        "evicted" => "⚪",
        _ => "🔵"
    };

    static string TruncateName(string name, int max) =>
        name.Length <= max ? name : name[..(max - 1)] + "…";

    static string FormatAge(string timestamp)
    {
        if (DateTime.TryParse(timestamp, out var dt))
        {
            var age = DateTime.UtcNow - dt.ToUniversalTime();
            if (age.TotalDays >= 1) return $"{(int)age.TotalDays}d";
            if (age.TotalHours >= 1) return $"{(int)age.TotalHours}h";
            return $"{(int)age.TotalMinutes}m";
        }
        return timestamp.Length > 10 ? timestamp[..10] : timestamp;
    }

    static string RunKubectl(string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "kubectl",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return "";
            string stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(10000);
            return stdout;
        }
        catch
        {
            return "";
        }
    }

    static void SendCells(string id, List<CellData> cells)
    {
        var payload = new
        {
            type = "activate-response",
            id,
            cells = cells.Select(c => new { c.R, c.C, c.V }).ToArray()
        };
        SendJson(payload);
    }

    static void SendLog(string message)
    {
        SendJson(new { type = "log", message });
    }

    static void SendJson(object obj)
    {
        string json = JsonSerializer.Serialize(obj, JsonOpts);
        Console.WriteLine(json);
        Console.Out.Flush();
    }
}

record CellData(
    [property: JsonPropertyName("r")] int R,
    [property: JsonPropertyName("c")] int C,
    [property: JsonPropertyName("v")] string V
);
