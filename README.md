# NAME

[![NuGet](https://img.shields.io/nuget/v/BeSwarm.ResxSourceGenerator.svg)](https://www.nuget.org/packages/BeSwarm.ResxSourceGenerator/)
[![NuGet](https://img.shields.io/nuget/dt/BeSwarm.ResxSourceGenerator.svg)](https://www.nuget.org/packages/BeSwarm.ResxSourceGenerator/)


Generate strong typed variables from resources item

The major problem with resources is that they are accessed by item name via a string.
This is not type safe and can lead to errors such as:

Input errors.

The impossibility of having intellisense.

This package generate strong typed vars that provide secure access to resources.

Classical resx access:
```csharp
ressourcemanager.GetString("Item");
```
with this package:
```csharp
ResClass.Item();
```


## Usage

Add the package to your project.
```csharp

 <PropertyGroup>
   ...
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)\GeneratedFiles</CompilerGeneratedFilesOutputPath>
   ...
  </PropertyGroup>

 <ItemGroup>
   <PackageReference Include="BeSwarm.ResxSourceGenerator" Version="1.0.0" /> 
   <AdditionalFiles Include="**/*.resx" />
  </ItemGroup>
```

## Code Example
When ResSourceGenerator is executed, code is generated.

Sample: Project have resources named App.resx and App.fr.resx

App have one item named Item1 with value "Text"
and one item Item2 with value "Confirm delete {0} ?"

Two stong typed getters are generated:
```csharp
namespace LibPages.Resources;
public partial class AppRes
{      ....
       public global::System.Globalization.CultureInfo? Culture { get; set; }
       public  string? Item1()=>GetString("Item1","<?Item1?>",null);
       public  string? Item2(object? arg0)=>GetString("Item2","<?Item2?>",arg0);
}
```
For all strong typed getters, the first parameter is the name of the resource item.

The second parameter is the default value if the item is not found.
This is useful for detect missing translated resources.

The third parameter is the arguments of the item.



Usage:
With Dependency injection:
```csharp
services.AddScoped<AppRes>();
```
in razor page
```csharp
[Inject] AppRes  _appRes{ get; set; } = default!;
```

or with a clasic new

```csharp
   AppRes _appRes=new();
```
access to resources items
```csharp
_appRes.Item1();  // return "Text"
_appRes.Item2("test"); //return Confirm delete test ?
```

Nota: By default the culture is the default context culture.
It is possible to change the culture with the property Culture.

```csharp
_appRes.Culture=CultureInfo.GetCultureInfo("fr");
```


## Company
Be Swarm https://beswarm.fr/developpeur_en/

## Author
thierry roustan


## License
MIT

    
## Versions
- 1.0.0
  - Initial release


 
 