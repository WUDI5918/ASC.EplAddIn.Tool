using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace AutoSchematicConfigurator.EplAddIn.Tool
{
    public class LibraryVersionInfo
    {
        public string VersionId { get; set; } = Guid.NewGuid().ToString("N");
        public int VersionNumber { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Description { get; set; } = "";
        public bool IsPublished { get; set; }
        public string SnapshotJson { get; set; } = "";
    }

    public static class LibraryVersionService
    {
        private static string GetVersionsDirectory()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "ASC.EplAddIn.Tool", "versions");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        private static string GetVersionFilePath(string versionId)
        {
            return Path.Combine(GetVersionsDirectory(), versionId + ".json");
        }

        public static LibraryVersionInfo CreateVersion(ConfiguratorData db, string description)
        {
            var versions = ListVersions();
            var version = new LibraryVersionInfo
            {
                VersionNumber = versions.Count > 0 ? versions.Max(v => v.VersionNumber) + 1 : 1,
                Description = description,
                SnapshotJson = JsonConvert.SerializeObject(db, Formatting.Indented)
            };
            File.WriteAllText(GetVersionFilePath(version.VersionId), JsonConvert.SerializeObject(version, Formatting.Indented));
            
            // 限制最多 50 个版本
            EnforceVersionLimit();
            
            return version;
        }

        public static List<LibraryVersionInfo> ListVersions()
        {
            var dir = GetVersionsDirectory();
            if (!Directory.Exists(dir)) return new List<LibraryVersionInfo>();

            var versions = new List<LibraryVersionInfo>();
            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    var version = JsonConvert.DeserializeObject<LibraryVersionInfo>(File.ReadAllText(file));
                    if (version != null) versions.Add(version);
                }
                catch (Exception ex)
                {
                    // Skip corrupt/unreadable version files instead of failing the whole listing.
                    // Logged so silent data corruption does not go unnoticed.
                    ConfiguratorService.LogDebug($"跳过损坏或不可读的版本文件 '{file}': {ex.Message}");
                }
            }
            return versions.OrderByDescending(v => v.Timestamp).ToList();
        }

        public static ConfiguratorData RestoreVersion(string versionId)
        {
            var path = GetVersionFilePath(versionId);
            if (!File.Exists(path)) return null;
            
            var version = JsonConvert.DeserializeObject<LibraryVersionInfo>(File.ReadAllText(path));
            if (version == null || string.IsNullOrEmpty(version.SnapshotJson)) return null;
            
            return JsonConvert.DeserializeObject<ConfiguratorData>(version.SnapshotJson);
        }

        public static void DeleteVersion(string versionId)
        {
            var path = GetVersionFilePath(versionId);
            if (File.Exists(path)) File.Delete(path);
        }

        public static void PublishVersion(string versionId)
        {
            var path = GetVersionFilePath(versionId);
            if (!File.Exists(path)) return;
            var version = JsonConvert.DeserializeObject<LibraryVersionInfo>(File.ReadAllText(path));
            if (version != null)
            {
                version.IsPublished = true;
                File.WriteAllText(path, JsonConvert.SerializeObject(version, Formatting.Indented));
            }
        }

        public static void RevokeVersion(string versionId)
        {
            var path = GetVersionFilePath(versionId);
            if (!File.Exists(path)) return;
            var version = JsonConvert.DeserializeObject<LibraryVersionInfo>(File.ReadAllText(path));
            if (version != null)
            {
                version.IsPublished = false;
                File.WriteAllText(path, JsonConvert.SerializeObject(version, Formatting.Indented));
            }
        }

        private static void EnforceVersionLimit()
        {
            var versions = ListVersions();
            if (versions.Count <= 50) return;
            
            // 删除最旧的未发布版本
            var toDelete = versions.Where(v => !v.IsPublished).OrderBy(v => v.Timestamp).Take(versions.Count - 50).ToList();
            foreach (var v in toDelete) DeleteVersion(v.VersionId);
        }
    }
}
