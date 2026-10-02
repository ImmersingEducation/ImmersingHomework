using System.Threading.Tasks;
using ImmersingHomework.Models;

namespace ImmersingHomework.Abstractions;

/// <summary>一言（Hitokoto）内容获取。</summary>
public interface IHitokotoService
{
    /// <summary>获取一条一言，失败时返回 <c>null</c>。</summary>
    Task<Hitokoto?> GetHitokoto();
}