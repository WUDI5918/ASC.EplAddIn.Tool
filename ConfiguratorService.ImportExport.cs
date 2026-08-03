using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using OfficeOpenXml;

namespace AutoSchematicConfigurator.EplAddIn.Tool
{
    public partial class ConfiguratorService
    {
        /// <summary>
        /// Exports the full ConfiguratorData library to an XLSX file with three sheets:
        /// MacroTypicals, TypicalGroups, Configurators.
        /// </summary>
        public static void ExportFullConfigToXlsx(ConfiguratorData db, string filePath)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));

            using (var package = new ExcelPackage())
            {
                // Sheet 1: MacroTypicals
                var ws1 = package.Workbook.Worksheets.Add("MacroTypicals");
                ws1.Cells[1, 1].Value = "Name";
                ws1.Cells[1, 2].Value = "Variables (JSON)";
                ws1.Cells[1, 3].Value = "Elements (JSON)";
                ws1.Cells[1, 1].Style.Font.Bold = true;
                ws1.Cells[1, 2].Style.Font.Bold = true;
                ws1.Cells[1, 3].Style.Font.Bold = true;

                int row = 2;
                if (db.MacroTypicals != null)
                {
                    foreach (var mt in db.MacroTypicals)
                    {
                        ws1.Cells[row, 1].Value = mt.Name;
                        ws1.Cells[row, 2].Value = JsonConvert.SerializeObject(mt.Variables);
                        ws1.Cells[row, 3].Value = JsonConvert.SerializeObject(mt.Elements);
                        row++;
                    }
                }

                // Sheet 2: TypicalGroups
                var ws2 = package.Workbook.Worksheets.Add("TypicalGroups");
                ws2.Cells[1, 1].Value = "Name";
                ws2.Cells[1, 2].Value = "Variables (JSON)";
                ws2.Cells[1, 3].Value = "Instances (JSON)";
                ws2.Cells[1, 1].Style.Font.Bold = true;
                ws2.Cells[1, 2].Style.Font.Bold = true;
                ws2.Cells[1, 3].Style.Font.Bold = true;

                row = 2;
                if (db.TypicalGroups != null)
                {
                    foreach (var tg in db.TypicalGroups)
                    {
                        ws2.Cells[row, 1].Value = tg.Name;
                        ws2.Cells[row, 2].Value = JsonConvert.SerializeObject(tg.Variables);
                        ws2.Cells[row, 3].Value = JsonConvert.SerializeObject(tg.Instances);
                        row++;
                    }
                }

                // Sheet 3: Configurators
                var ws3 = package.Workbook.Worksheets.Add("Configurators");
                ws3.Cells[1, 1].Value = "Name";
                ws3.Cells[1, 2].Value = "Description";
                ws3.Cells[1, 3].Value = "TypicalGroupRefs (JSON)";
                ws3.Cells[1, 1].Style.Font.Bold = true;
                ws3.Cells[1, 2].Style.Font.Bold = true;
                ws3.Cells[1, 3].Style.Font.Bold = true;

                row = 2;
                if (db.Configurators != null)
                {
                    foreach (var cfg in db.Configurators)
                    {
                        ws3.Cells[row, 1].Value = cfg.Name;
                        ws3.Cells[row, 2].Value = cfg.Description;
                        ws3.Cells[row, 3].Value = JsonConvert.SerializeObject(cfg.TypicalGroupRefs);
                        row++;
                    }
                }

                // Auto-fit columns for all sheets
                foreach (var ws in package.Workbook.Worksheets)
                {
                    ws.Cells.AutoFitColumns();
                }

                package.SaveAs(new FileInfo(filePath));
            }
        }

        /// <summary>
        /// Imports a full ConfiguratorData library from an XLSX file.
        /// Expects three sheets: MacroTypicals, TypicalGroups, Configurators.
        /// </summary>
        public static ConfiguratorData ImportFullConfigFromXlsx(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));
            if (!File.Exists(filePath)) throw new FileNotFoundException("XLSX file not found.", filePath);

            var db = new ConfiguratorData();

            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                // Sheet 1: MacroTypicals
                var ws1 = package.Workbook.Worksheets.FirstOrDefault(w => w.Name == "MacroTypicals");
                if (ws1 != null)
                {
                    int rowCount = ws1.Dimension?.End.Row ?? 1;
                    for (int r = 2; r <= rowCount; r++)
                    {
                        string name = ws1.Cells[r, 1].Text?.Trim();
                        if (string.IsNullOrEmpty(name)) continue;

                        var mt = new MacroTypical { Name = name };
                        string varsJson = ws1.Cells[r, 2].Text;
                        string elemsJson = ws1.Cells[r, 3].Text;

                        if (!string.IsNullOrEmpty(varsJson))
                            mt.Variables = JsonConvert.DeserializeObject<List<ConfigurationVariable>>(varsJson) ?? new List<ConfigurationVariable>();
                        if (!string.IsNullOrEmpty(elemsJson))
                            mt.Elements = JsonConvert.DeserializeObject<List<MacroElement>>(elemsJson) ?? new List<MacroElement>();

                        db.MacroTypicals.Add(mt);
                    }
                }

                // Sheet 2: TypicalGroups
                var ws2 = package.Workbook.Worksheets.FirstOrDefault(w => w.Name == "TypicalGroups");
                if (ws2 != null)
                {
                    int rowCount = ws2.Dimension?.End.Row ?? 1;
                    for (int r = 2; r <= rowCount; r++)
                    {
                        string name = ws2.Cells[r, 1].Text?.Trim();
                        if (string.IsNullOrEmpty(name)) continue;

                        var tg = new TypicalGroup { Name = name };
                        string varsJson = ws2.Cells[r, 2].Text;
                        string instsJson = ws2.Cells[r, 3].Text;

                        if (!string.IsNullOrEmpty(varsJson))
                            tg.Variables = JsonConvert.DeserializeObject<List<ConfigurationVariable>>(varsJson) ?? new List<ConfigurationVariable>();
                        if (!string.IsNullOrEmpty(instsJson))
                            tg.Instances = JsonConvert.DeserializeObject<List<TypicalInstance>>(instsJson) ?? new List<TypicalInstance>();

                        db.TypicalGroups.Add(tg);
                    }
                }

                // Sheet 3: Configurators
                var ws3 = package.Workbook.Worksheets.FirstOrDefault(w => w.Name == "Configurators");
                if (ws3 != null)
                {
                    int rowCount = ws3.Dimension?.End.Row ?? 1;
                    for (int r = 2; r <= rowCount; r++)
                    {
                        string name = ws3.Cells[r, 1].Text?.Trim();
                        if (string.IsNullOrEmpty(name)) continue;

                        var cfg = new Configurator
                        {
                            Name = name,
                            Description = ws3.Cells[r, 2].Text ?? ""
                        };
                        string refsJson = ws3.Cells[r, 3].Text;

                        if (!string.IsNullOrEmpty(refsJson))
                            cfg.TypicalGroupRefs = JsonConvert.DeserializeObject<List<string>>(refsJson) ?? new List<string>();

                        db.Configurators.Add(cfg);
                    }
                }
            }

            return db;
        }

        /// <summary>
        /// Exports the full ConfiguratorData library to a JSON file (XML alternative).
        /// </summary>
        public static void ExportFullConfigToJson(ConfiguratorData db, string filePath)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));

            string json = JsonConvert.SerializeObject(db, Formatting.Indented);
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Imports the full ConfiguratorData library from a JSON file.
        /// </summary>
        public static ConfiguratorData ImportFullConfigFromJson(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));
            if (!File.Exists(filePath)) throw new FileNotFoundException("JSON file not found.", filePath);

            string json = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<ConfiguratorData>(json);
        }
    }
}
