using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using TiaOpennessApp.Models.Generation;

namespace TiaOpennessApp.Services
{
    public class CsvGenerationWriter
    {
        private readonly string _docsDir;

        public CsvGenerationWriter(string docsDir)
        {
            _docsDir = docsDir;
        }

        public void Write(GenerationDocument doc)
        {
            if (!Directory.Exists(_docsDir))
                Directory.CreateDirectory(_docsDir);

            WriteDevices(doc.Devices);
            WriteModules(doc.Modules);
            WriteSubnets(doc.Subnets);
            WriteInterfaces(doc.Interfaces);
        }

        private void WriteDevices(List<DeviceSpec> devices)
        {
            string path = Path.Combine(_docsDir, "Devices.csv");
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            using (var csv = new CsvWriter(writer, CreateConfig()))
            {
                csv.WriteField(GenerationColumns.DeviceName);
                csv.WriteField(GenerationColumns.Group);
                csv.WriteField(GenerationColumns.TypeKey);
                csv.WriteField(GenerationColumns.Firmware);
                csv.WriteField(GenerationColumns.Comment);
                csv.NextRecord();

                foreach (var d in devices)
                {
                    csv.WriteField(d.DeviceName);
                    csv.WriteField(d.Group);
                    csv.WriteField(d.TypeKey);
                    csv.WriteField(d.Firmware);
                    csv.WriteField(d.Comment);
                    csv.NextRecord();
                }
            }
        }

        private void WriteModules(List<ModuleSpec> modules)
        {
            string path = Path.Combine(_docsDir, "Modules.csv");
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            using (var csv = new CsvWriter(writer, CreateConfig()))
            {
                csv.WriteField(GenerationColumns.Device);
                csv.WriteField(GenerationColumns.ModuleName);
                csv.WriteField(GenerationColumns.Slot);
                csv.WriteField(GenerationColumns.TypeKey);
                csv.WriteField(GenerationColumns.Comment);
                csv.NextRecord();

                foreach (var m in modules)
                {
                    csv.WriteField(m.Device);
                    csv.WriteField(m.ModuleName);
                    csv.WriteField(m.Slot);
                    csv.WriteField(m.TypeKey);
                    csv.WriteField(m.Comment);
                    csv.NextRecord();
                }
            }
        }

        private void WriteSubnets(List<SubnetSpec> subnets)
        {
            string path = Path.Combine(_docsDir, "Subnets.csv");
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            using (var csv = new CsvWriter(writer, CreateConfig()))
            {
                csv.WriteField(GenerationColumns.Name);
                csv.WriteField(GenerationColumns.SubnetType);
                csv.NextRecord();

                foreach (var s in subnets)
                {
                    csv.WriteField(s.Name);
                    csv.WriteField(s.SubnetType);
                    csv.NextRecord();
                }
            }
        }

        private void WriteInterfaces(List<InterfaceSpec> interfaces)
        {
            string path = Path.Combine(_docsDir, "Interfaces.csv");
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            using (var csv = new CsvWriter(writer, CreateConfig()))
            {
                csv.WriteField(GenerationColumns.Device);
                csv.WriteField(GenerationColumns.InterfaceItem);
                csv.WriteField(GenerationColumns.Subnet);
                csv.WriteField(GenerationColumns.IpAddress);
                csv.WriteField(GenerationColumns.SubnetMask);
                csv.WriteField(GenerationColumns.PnDeviceName);
                csv.WriteField(GenerationColumns.MasterIoSystem);
                csv.NextRecord();

                foreach (var i in interfaces)
                {
                    csv.WriteField(i.Device);
                    csv.WriteField(i.InterfaceItem);
                    csv.WriteField(i.Subnet);
                    csv.WriteField(i.IpAddress);
                    csv.WriteField(i.SubnetMask);
                    csv.WriteField(i.PnDeviceName);
                    csv.WriteField(i.MasterIoSystem);
                    csv.NextRecord();
                }
            }
        }

        private static CsvConfiguration CreateConfig()
        {
            return new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                TrimOptions = TrimOptions.Trim,
            };
        }
    }
}
