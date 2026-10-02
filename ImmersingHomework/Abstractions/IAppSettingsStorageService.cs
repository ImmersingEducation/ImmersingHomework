using System.Threading.Tasks;
using ImmersingHomework.Models;

namespace ImmersingHomework.Abstractions;

/// <summary>应用设置（<see cref="AppSettings"/>）的持久化读写。</summary>
public interface IAppSettingsStorageService
{
    bool Exists();

    void Save(AppSettings settings);

    Task SaveAsync(AppSettings settings);

    AppSettings Load();
}