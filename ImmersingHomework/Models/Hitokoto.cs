namespace ImmersingHomework.Models;

/// <summary>一条一言内容。</summary>
/// <param name="Sentence">正文。</param>
/// <param name="Author">作者/出处。</param>
public readonly record struct Hitokoto(string Sentence, string Author);