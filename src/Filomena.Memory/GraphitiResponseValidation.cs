using System.Text.Json;

namespace Filomena.Memory;

// Controlled diagnostic metadata only; never includes backend exception text or memory contents.
internal static class GraphitiResponseValidation
{
    internal static InvalidDataException Failure(string stage, string code)
    {
        var error = new InvalidDataException(code);
        error.Data["graphiti_stage"] = stage;
        error.Data["graphiti_code"] = code;
        return error;
    }

    internal static JsonElement Facts(JsonElement value, string ns, Action<string, bool, string>? report = null)
    {
        void Pass(string stage) => report?.Invoke(stage, true, "validated");
        void Reject(string stage, string code)
        {
            report?.Invoke(stage, false, code);
            throw Failure(stage, code);
        }
        Pass("transport_valid"); // A response reached this boundary, not proof of backend success.
        var root = value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Count() == 1
            && value.TryGetProperty("result", out var result) ? result : value;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error))
        {
            if (error.ValueKind != JsonValueKind.String) Reject("response_schema_valid", "response_schema_invalid");
            Pass("response_schema_valid"); // ErrorResponse is part of the actual MCP union contract.
            Reject("backend_read_success", "backend_read_error");
        }
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("facts", out var array) || array.ValueKind != JsonValueKind.Array)
            Reject("response_schema_valid", "response_schema_invalid");
        var facts = root.GetProperty("facts");
        if (facts.EnumerateArray().Any(f => f.ValueKind != JsonValueKind.Object)) Reject("response_schema_valid", "response_schema_invalid");
        Pass("response_schema_valid");
        Pass("backend_read_success");
        foreach (var fact in facts.EnumerateArray())
        {
            if (!fact.TryGetProperty("uuid", out var id) || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()))
                Reject("result_identity_valid", "fact_identity_missing");
            if (!fact.TryGetProperty("group_id", out var group) || group.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(group.GetString()))
                Reject("result_namespace_valid", "namespace_provenance_missing");
            if (group.GetString() != ns) Reject("result_namespace_valid", "confirmed_cross_namespace_leakage");
        }
        Pass("result_identity_valid");
        Pass("result_namespace_valid");
        return root;
    }
}
