using System;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaOpennessApp.Models;

namespace TiaOpennessApp.Services
{
    public class ProjectService
    {
        private readonly TiaConnection _connection;

        public ProjectService(TiaConnection connection)
        {
            _connection = connection;
        }

        public ProjectMetadata GetProjectInfo()
        {
            var project = _connection.Project;
            return new ProjectMetadata
            {
                Name = project.Name,
                Path = project.Path.FullName,
                Version = project.Version,
                CreationTime = project.CreationTime,
                LastModified = project.LastModified,
            };
        }

        public void PrintProjectInfo()
        {
            var project = _connection.Project;
            Console.WriteLine($"  Name:            {project.Name}");
            Console.WriteLine($"  Path:            {project.Path.FullName}");
            Console.WriteLine($"  Version:         {project.Version}");
            Console.WriteLine($"  Created:         {project.CreationTime}");
            Console.WriteLine($"  Last Modified:   {project.LastModified}");
            Console.WriteLine($"  Device Count:    {project.Devices.Count}");

            int totalItems = project.Devices.Sum(d => d.DeviceItems.Count);
            Console.WriteLine($"  Total HW Items:  {totalItems}");
        }

        public int GetDeviceCount()
        {
            return _connection.Project.Devices.Count;
        }
    }
}
