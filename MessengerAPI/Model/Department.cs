using System;
using System.Collections.Generic;

namespace MessengerAPI.Model;

public partial class Department
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public int? ParentDepartmentId { get; set; }

    public int? ChatId { get; set; }

    public virtual Chat? Chat { get; set; }

    public virtual ICollection<Department> InverseParentDepartment { get; set; } = new List<Department>();

    public virtual Department? ParentDepartment { get; set; }

    public virtual ICollection<User> Users { get; set; } = new List<User>();
}
