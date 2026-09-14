using System;
using System.Linq;
using Siemens.Engineering;

namespace TiaOpennessApp.Services
{
    public class ProjectService
    {
        private readonly TiaConnection _connection;

        public ProjectService(TiaConnection connection)
        {
            _connection = connection;
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
    }
}