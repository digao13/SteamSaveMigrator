using System;
using System.Collections.Generic;
using System.Linq;

namespace SteamSaveMigrator.Core.Parsers;

/// <summary>
/// Representa um nó na árvore KeyValues (VDF / ACF) da Valve.
/// Pode ser um valor escalar (chave -> valor) ou um nó composto com filhos (chave -> { filhos }).
/// </summary>
public class VdfNode
{
    public string Name { get; set; } = string.Empty;
    public string? Value { get; set; }
    public List<VdfNode> Children { get; } = new();

    public bool IsCompound => Children.Count > 0;

    public VdfNode(string name, string? value = null)
    {
        Name = name;
        Value = value;
    }

    public VdfNode? this[string childName] =>
        Children.FirstOrDefault(c => string.Equals(c.Name, childName, StringComparison.OrdinalIgnoreCase));

    public VdfNode? GetChild(string childName) => this[childName];

    public IEnumerable<VdfNode> GetChildren(string childName) =>
        Children.Where(c => string.Equals(c.Name, childName, StringComparison.OrdinalIgnoreCase));

    public string? GetString(string childName, string? defaultValue = null)
    {
        var child = this[childName];
        return child?.Value ?? defaultValue;
    }

    public int GetInt32(string childName, int defaultValue = 0)
    {
        var str = GetString(childName);
        return int.TryParse(str, out var result) ? result : defaultValue;
    }

    public uint GetUInt32(string childName, uint defaultValue = 0)
    {
        var str = GetString(childName);
        return uint.TryParse(str, out var result) ? result : defaultValue;
    }

    public long GetInt64(string childName, long defaultValue = 0)
    {
        var str = GetString(childName);
        return long.TryParse(str, out var result) ? result : defaultValue;
    }

    public ulong GetUInt64(string childName, ulong defaultValue = 0)
    {
        var str = GetString(childName);
        return ulong.TryParse(str, out var result) ? result : defaultValue;
    }

    public bool GetBoolean(string childName, bool defaultValue = false)
    {
        var str = GetString(childName);
        if (str == null) return defaultValue;
        if (str == "1" || str.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (str == "0" || str.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        return defaultValue;
    }

    public void AddChild(VdfNode child) => Children.Add(child);

    public override string ToString()
    {
        return IsCompound
            ? $"\"{Name}\" [{Children.Count} children]"
            : $"\"{Name}\" = \"{Value}\"";
    }
}
