using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.CommandLine;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

/// <summary>
/// AssemblyPublicizer - A tool to create a copy of an assembly in 
/// which all members are public (types, methods, fields, getters
/// and setters of properties).  
/// 
/// Copyright(c) 2018 CabbageCrow
/// This library is free software; you can redistribute it and/or
/// modify it under the terms of the GNU Lesser General Public
/// License as published by the Free Software Foundation; either
/// version 2.1 of the License, or(at your option) any later version.
/// 
/// Overview:
/// https://tldrlegal.com/license/gnu-lesser-general-public-license-v2.1-(lgpl-2.1)
/// 
/// This library is distributed in the hope that it will be useful,
/// but WITHOUT ANY WARRANTY; without even the implied warranty of
///	MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the GNU
/// Lesser General Public License for more details.
/// 
/// You should have received a copy of the GNU Lesser General Public
/// License along with this library; if not, write to the Free Software
/// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301
/// USA
/// </summary>

namespace Shibowo.AssemblyRePublicizer
{
  /// <summary>
  /// Creates a copy of an assembly in which all members are public (types, methods, fields, getters and setters of properties).
  /// If you use the modified assembly as your reference and compile your dll with the option "Allow unsafe code" enabled, 
  /// you can access all private elements even when using the original assembly.
  /// Without "Allow unsafe code" you get an access violation exception during runtime when accessing private members except for types.  
  /// How to enable it: https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/unsafe-compiler-option
  /// arg 0 / -i|--input:		Path to the assembly (absolute or relative)
  /// arg 1 / -o|--output:	[Optional] Output path/filename
  ///							Can be just a (relative) path like "subdir1\subdir2"
  ///							Can be just a filename like "CustomFileName.dll"
  ///							Can be a filename with path like "C:\subdir1\subdir2\CustomFileName.dll"
  /// </summary>
  class AssemblyPublicizer
  {
    static bool automaticExit, help;

    static void Main(string[] args)
    {
      var suffix = "_publicized";
      var defaultOutputDir = "publicized_assemblies";
      string input = "";
      string output = "";

      //parse the command line
      Option<string> inputOption = new
        (
         aliases: new[] {"-i", "--input"},
         getDefaultValue: () => "",
         description: "Path (relative or absolute) to the input assembly"
        );
      Option<string> outputOption = new
        (
         aliases: new[] {"-o", "--output"},
         getDefaultValue: () => "",
         description: "Path (relative or absolute) to the input assembly"
        );
      Option<bool> exitOption = new
        (
         aliases: new[] {"-e", "--exit"},
         getDefaultValue: () => false,
         description: "Application should automatically exit"
        );     
      Option<bool> helpOption = new
        (
         aliases: new[] {"-h", "--help"},
         getDefaultValue: () => false,
         description: "Show this message"
        );

      RootCommand rootCommand = new();
      rootCommand.Add(inputOption);
      rootCommand.Add(outputOption);
      rootCommand.Add(exitOption);
      rootCommand.Add(helpOption);

      rootCommand.SetHandler((inputDirectory, outputDirectory, autoExit, getHelp) => 
          {
          input = inputDirectory;
          output = outputDirectory;
          automaticExit = autoExit;
          help = getHelp;
          }, inputOption, outputOption, exitOption, helpOption);

      rootCommand.Invoke(args);

      Console.WriteLine();

      try
      {
        if (help)
          ShowHelp();

        if (input == "")
          throw new ArgumentException("Input cannot be empty!");
      }
      catch (ArgumentException)
      {
        // output some error message
        Console.WriteLine("ERROR! Incorrect arguments. You need to provide the path to the assembly to publicize.");
        Console.WriteLine("On Windows you can even drag and drop the assembly on the .exe.");
        Console.WriteLine("Try `--help' for more information.");
        Exit(10);
      }


      var inputFile = input;

      string outputPath = "", outputName = "";
      if (output != "")
      {
        try
        {
          outputPath = Path.GetDirectoryName(output);
          outputName = Path.GetFileName(output);
        }
        catch(Exception)
        {
          Console.WriteLine("ERROR! Invalid output argument.");
          Exit(20);
        }
      }


      if (!File.Exists(inputFile))
      {
        Console.WriteLine();
        Console.WriteLine("ERROR! File doesn't exist or you don't have sufficient permissions.");
        Exit(30);
      }

      ModuleContext moduleCtx = ModuleDef.CreateModuleContext();
      ModuleDefMD module = ModuleDefMD.Load(input, moduleCtx);


      var allTypes = module.GetTypes();
      var allMethods = allTypes.SelectMany(t => t.Methods);
      var allFields = allTypes.SelectMany(t => t.Fields);

      int count;
      string reportString = "Changed {0} {1} to public.";

      #region Make everything public

      count = 0;
      foreach (var type in allTypes)
      {
        if (!type?.IsPublic ?? false && !type.IsNestedPublic)
        {
          count++;
          if (type.IsNested){
            type.Attributes &= ~TypeAttributes.VisibilityMask;
            type.Attributes |= TypeAttributes.NestedPublic;
          }
          else{
            type.Attributes &= ~TypeAttributes.VisibilityMask;
            type.Attributes |= TypeAttributes.Public;
          }
        }
      }
      Console.WriteLine(reportString, count, "types");

      count = 0;
      foreach (var method in allMethods)
      {
        if (!method?.IsPublic ?? false)
        {
          count++;
          method.Attributes &= ~MethodAttributes.MemberAccessMask;
          method.Attributes |= MethodAttributes.Public;
        }
      }
      Console.WriteLine(reportString, count, "methods (including getters and setters)");

      count = 0;
      foreach (var field in allFields)
      {
        if (!field?.IsPublic ?? false)
        {
          count++;
          field.Attributes &= ~FieldAttributes.FieldAccessMask;
          field.Attributes |= FieldAttributes.Public;
        }
      }
      Console.WriteLine(reportString, count, "fields");

      #endregion


      Console.WriteLine();

      if (outputName == "")
      {
        outputName = String.Format("{0}{1}{2}",
            Path.GetFileNameWithoutExtension(inputFile), suffix, Path.GetExtension(inputFile));
        Console.WriteLine(@"Info: Use default output name: ""{0}""", outputName);
      }

      if(outputPath == "")
      {
        outputPath = defaultOutputDir;
        Console.WriteLine(@"Info: Use default output dir: ""{0}""", outputPath);
      }

      Console.WriteLine("Saving a copy of the modified assembly ...");
      var outputFile = Path.Combine(outputPath, outputName);
      Console.WriteLine($"Saving in: {outputFile}");
      try
      {
        if (outputPath != "" && !Directory.Exists(outputPath))
          Directory.CreateDirectory(outputPath);
        if(module.IsILOnly){
          Console.WriteLine("Module is IL-only");
          module.Write(outputFile);
        }
        else{
          Console.WriteLine("Module contains native code");
          Console.Error.WriteLine("WARNING: Using this tool with non-IL-only assemblies is untested.");
          module.NativeWrite(outputFile);
        }
      }
      catch (Exception)
      {
        Console.WriteLine();
        Console.WriteLine("ERROR! Cannot create/overwrite the new assembly. ");
        Console.WriteLine("Please check the path and its permissions " +
            "and in case of overwriting an existing file ensure that it isn't currently used.");
        Exit(50);
      }

      Console.WriteLine("Completed.");
      Console.WriteLine();
      Console.WriteLine("Use the publicized library as your reference and compile your dll with the ");
      Console.WriteLine(@"option ""Allow unsafe code"" enabled.");
      Console.WriteLine(@"Without it you get an access violation exception during runtime when accessing");
      Console.WriteLine("private members except for types.");
      Exit(0);
    }

    public static void Exit(int exitCode = 0)
    {
      if (!automaticExit)
      {
        Console.WriteLine();
        Console.WriteLine("Press any key to exit ...");
        Console.ReadKey();
      }
      Environment.Exit(exitCode);
    }

    private static void ShowHelp()
    {
      Console.WriteLine("Usage: AssemblyPublicizer.exe --input {ASSEMBLY_PATH} {OPTIONAL_OPTS}");
      Console.WriteLine("Creates a copy of an assembly in which all members are public.");
      Console.WriteLine("An input path must be provided, the other options are optional.");
      Console.WriteLine("If so, the first argument is for input and the optional second one for output.");
      Console.WriteLine();	
      Exit(0);
    }

  }
}
