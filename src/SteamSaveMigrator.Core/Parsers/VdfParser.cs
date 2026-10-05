using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SteamSaveMigrator.Core.Parsers;

/// <summary>
/// Parser para arquivos no formato KeyValues / VDF / ACF da Valve.
/// </summary>
public static class VdfParser
{
    public static VdfNode ParseFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Arquivo VDF não encontrado: {filePath}", filePath);

        var content = File.ReadAllText(filePath, Encoding.UTF8);
        return ParseText(content);
    }

    public static VdfNode ParseText(string text)
    {
        var root = new VdfNode("root");
        var tokens = Tokenize(text);
        int index = 0;

        while (index < tokens.Count)
        {
            var node = ParseNode(tokens, ref index);
            if (node != null)
            {
                root.AddChild(node);
            }
        }

        // Se houver apenas um nó filho na raiz, podemos retornar esse nó ou a raiz.
        return root.Children.Count == 1 ? root.Children[0] : root;
    }

    private static VdfNode? ParseNode(List<string> tokens, ref int index)
    {
        if (index >= tokens.Count) return null;

        var key = tokens[index++];
        if (key == "}")
        {
            // Fim do bloco
            return null;
        }

        if (index >= tokens.Count)
        {
            return new VdfNode(key);
        }

        var next = tokens[index];

        if (next == "{")
        {
            // Início de bloco de filhos
            index++; // pula o "{"
            var compoundNode = new VdfNode(key);

            while (index < tokens.Count && tokens[index] != "}")
            {
                var child = ParseNode(tokens, ref index);
                if (child != null)
                {
                    compoundNode.AddChild(child);
                }
            }

            if (index < tokens.Count && tokens[index] == "}")
            {
                index++; // pula o "}"
            }

            return compoundNode;
        }
        else
        {
            // Valor escalar
            var value = tokens[index++];
            return new VdfNode(key, value);
        }
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        int i = 0;
        int length = text.Length;

        while (i < length)
        {
            char c = text[i];

            // Pula whitespace
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            // Pula comentários //
            if (c == '/' && i + 1 < length && text[i + 1] == '/')
            {
                i += 2;
                while (i < length && text[i] != '\n' && text[i] != '\r')
                {
                    i++;
                }
                continue;
            }

            // Chaves { ou }
            if (c == '{' || c == '}')
            {
                tokens.Add(c.ToString());
                i++;
                continue;
            }

            // String entre aspas
            if (c == '"')
            {
                i++; // pula aspas de abertura
                var sb = new StringBuilder();
                bool escaped = false;

                while (i < length)
                {
                    char sc = text[i];
                    if (escaped)
                    {
                        if (sc == 'n') sb.Append('\n');
                        else if (sc == 't') sb.Append('\t');
                        else if (sc == '\\') sb.Append('\\');
                        else if (sc == '"') sb.Append('"');
                        else sb.Append(sc);
                        escaped = false;
                        i++;
                    }
                    else if (sc == '\\')
                    {
                        escaped = true;
                        i++;
                    }
                    else if (sc == '"')
                    {
                        i++; // fecha aspas
                        break;
                    }
                    else
                    {
                        sb.Append(sc);
                        i++;
                    }
                }

                tokens.Add(sb.ToString());
                continue;
            }

            // Palavra simples sem aspas
            var wordSb = new StringBuilder();
            while (i < length && !char.IsWhiteSpace(text[i]) && text[i] != '{' && text[i] != '}' && text[i] != '"')
            {
                wordSb.Add(text[i]);
                i++;
            }
            if (wordSb.Length > 0)
            {
                tokens.Add(wordSb.ToString());
            }
        }

        return tokens;
    }

    private static void Add(this StringBuilder sb, char c) => sb.Append(c);
}
