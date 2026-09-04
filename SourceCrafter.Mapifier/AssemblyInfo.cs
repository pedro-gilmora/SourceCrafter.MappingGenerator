global using RenderFlags = (bool defaultMethod, bool fillMethod, bool tryGetMethod, bool tryFill);
global using ScalarConversion = (bool exists, bool isExplicit);

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// The harness drives the generator's internal entry point directly so its output can be diffed
// against the checked-in baseline without spinning up a full compilation.
[assembly: InternalsVisibleTo("NewGenerator")]

// In SDK-style projects such as this one, several assembly attributes that were historically
// defined in this file are now automatically added during build and populated with
// values defined in project properties. For details of which attributes are included
// and how to customise this process see: https://aka.ms/assembly-info-properties


// Setting ComVisible to false makes the types in this assembly not visible to COM
// components.  If you need to access a type in this assembly from COM, set the ComVisible
// attribute to true on that type.

[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM.

[assembly: Guid("212c224f-a04f-4cd1-bfaa-f97d0c760cf6")]

