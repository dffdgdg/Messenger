using System;
using System.Globalization;
using MessengerDesktop.Converters.Base;
using MessengerShared.DTO;

namespace MessengerDesktop.Converters.Department
{
    public class DepartmentHierarchyConverter : ValueConverterBase<DepartmentDTO, string>
    {
        private const int IndentSize = 2;

        protected override string ConvertValue(DepartmentDTO department, object? parameter, CultureInfo culture)
        {
            if (department is null)
                return string.Empty;

            int level = GetDepartmentLevel(department);
            var prefix = new string('-', level * IndentSize);
            return string.IsNullOrEmpty(prefix) ? department.Name : $"{prefix} {department.Name}";
        }

        private static int GetDepartmentLevel(DepartmentDTO department)
        {
            int level = 0;
            var parentId = department.ParentDepartmentId;
            while (parentId.HasValue)
            {
                level++;
                if (level > 100) break;

                parentId = null;
            }
            return level;
        }

        protected override object? HandleConversionError(object? value) => string.Empty;
    }
}