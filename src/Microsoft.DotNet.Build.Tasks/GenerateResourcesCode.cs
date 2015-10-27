﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Text;

namespace Microsoft.DotNet.Build.Tasks
{
    public class GenerateResourcesCode : Task
    {
        private TargetLanguage _targetLanguage = TargetLanguage.CSharp;
        private StreamWriter _targetStream;
        private StringBuilder _debugCode = new StringBuilder();
        private Dictionary<string, int> _keys;
        private String _resourcesName;

        [Required]
        public string ResxFilePath { get; set; }

        [Required]
        public string OutputSourceFilePath { get; set; }

        [Required]
        public string AssemblyName { get; set; }

        public bool DebugOnly { get; set; }

        public override bool Execute()
        {
            try
            {
                _resourcesName = "FxResources." + AssemblyName;

                using (_targetStream = File.CreateText(OutputSourceFilePath))
                {
                    if (String.Equals(Path.GetExtension(OutputSourceFilePath), ".vb", StringComparison.OrdinalIgnoreCase))
                    {
                        _targetLanguage = TargetLanguage.VB;
                    }
                    _keys = new Dictionary<string, int>();
                    WriteClassHeader();
					RunOnResFile();
					WriteDebugCode();
					WriteGetTypeProperty();
					WriteClassEnd();
					WriteResourceTypeClass();
                }
            }
            catch (Exception e)
            {
                Log.LogError("Failed to generate the resource code with error:\n" + e.Message);
                return false; // fail the task
            }

            return true;
        }

        private void WriteClassHeader()
        {
            string commentPrefix = _targetLanguage == TargetLanguage.CSharp ? "// " : "' ";
            _targetStream.WriteLine(commentPrefix + "Do not edit this file manually it is auto-generated during the build based on the .resx file for this project.");

            if (_targetLanguage == TargetLanguage.CSharp)
            {
                _targetStream.WriteLine("namespace System");
                _targetStream.WriteLine("{");


                _targetStream.WriteLine("    internal static partial class SR");
                _targetStream.WriteLine("    {");

                _targetStream.WriteLine("#pragma warning disable 0414");
                _targetStream.WriteLine("        private const string s_resourcesName = \"{0}\";", _resourcesName + ".SR");
                _targetStream.WriteLine("#pragma warning restore 0414");
                _targetStream.WriteLine("");

                if (!DebugOnly)
                    _targetStream.WriteLine("#if !DEBUGRESOURCES");
            }
            else
            {
                _targetStream.WriteLine("Namespace System");

                _targetStream.WriteLine("    Friend Partial Class SR");
                _targetStream.WriteLine("    ");

                _targetStream.WriteLine("        Private Const s_resourcesName As String = \"{0}\"", _resourcesName + ".SR");
                _targetStream.WriteLine("");
                if (!DebugOnly)
                    _targetStream.WriteLine("#If Not DEBUGRESOURCES Then");
            }
        }

        private void RunOnResFile()
        {
            foreach(KeyValuePair<string, string> pair in GetResources(ResxFilePath))
            {
                StoreValues((string)pair.Key, (string)pair.Value);
            }
        }

        private void StoreValues(string leftPart, string rightPart)
        {
            int value;
            if (_keys.TryGetValue(leftPart, out value))
            {
                return;
            }
            _keys[leftPart] = 0;
            StringBuilder sb = new StringBuilder(rightPart.Length);
            for (var i = 0; i < rightPart.Length; i++)
            {
                // duplicate '"' for VB and C#
                if (rightPart[i] == '\"' && (_targetLanguage == TargetLanguage.VB || _targetLanguage == TargetLanguage.CSharp))
                {
                    sb.Append("\"");
                }
                sb.Append(rightPart[i]);
            }

            if (_targetLanguage == TargetLanguage.CSharp)
            {
                _debugCode.AppendFormat("        internal static string {0} {2}{4}              get {2} return SR.GetResourceString(\"{0}\", @\"{1}\"); {3}{4}        {3}{4}", leftPart, sb.ToString(), "{", "}", Environment.NewLine);
            }
            else
            {
                _debugCode.AppendFormat("        Friend Shared ReadOnly Property {0} As String{2}            Get{2}                Return SR.GetResourceString(\"{0}\", \"{1}\"){2}            End Get{2}        End Property{2}", leftPart, sb.ToString(), Environment.NewLine);
            }

            if (!DebugOnly)
            {
                if (_targetLanguage == TargetLanguage.CSharp)
                {
                    _targetStream.WriteLine("        internal static string {0} {2}{4}              get {2} return SR.GetResourceString(\"{0}\", {1}); {3}{4}        {3}", leftPart, "null", "{", "}", Environment.NewLine);
                }
                else
                {
                    _targetStream.WriteLine("        Friend Shared ReadOnly Property {0} As String{2}           Get{2}                 Return SR.GetResourceString(\"{0}\", {1}){2}            End Get{2}        End Property", leftPart, "Nothing", Environment.NewLine);
                }
            }
        }

        private void WriteDebugCode()
        {
            if (_targetLanguage == TargetLanguage.CSharp)
            {
                if (!DebugOnly)
                    _targetStream.WriteLine("#else");
                _targetStream.WriteLine(_debugCode.ToString());
                if (!DebugOnly)
                    _targetStream.WriteLine("#endif");
            }
            else
            {
                if (!DebugOnly)
                    _targetStream.WriteLine("#Else");
                _targetStream.WriteLine(_debugCode.ToString());
                if (!DebugOnly)
                    _targetStream.WriteLine("#End If");
            }
        }

        private void WriteGetTypeProperty()
        {
            if (_targetLanguage == TargetLanguage.CSharp)
            {
                _targetStream.WriteLine("        internal static Type ResourceType {1}{3}              get {1} return typeof({0}); {2}{3}        {2}", _resourcesName + ".SR", "{", "}", Environment.NewLine);
            }
            else
            {
                _targetStream.WriteLine("        Friend Shared ReadOnly Property ResourceType As Type{1}           Get{1}                 Return GetType({0}){1}            End Get{1}        End Property", _resourcesName + ".SR", Environment.NewLine);
            }
        }

        private void WriteClassEnd()
        {
            if (_targetLanguage == TargetLanguage.CSharp)
            {
                _targetStream.WriteLine("    }");
                _targetStream.WriteLine("}");
            }
            else
            {
                _targetStream.WriteLine("    End Class");
                _targetStream.WriteLine("End Namespace");
            }
        }

        private void WriteResourceTypeClass()
        {
            if (_targetLanguage == TargetLanguage.CSharp)
            {
                _targetStream.WriteLine("namespace {0}", _resourcesName);
                _targetStream.WriteLine("{");
                _targetStream.WriteLine("    // The type of this class is used to create the ResourceManager instance as the type name matches the name of the embedded resources file");
                _targetStream.WriteLine("    internal static class SR");
                _targetStream.WriteLine("    {");
                _targetStream.WriteLine("    }");
                _targetStream.WriteLine("}");
            }
            else
            {
                _targetStream.WriteLine("Namespace {0}", _resourcesName);
                _targetStream.WriteLine("    ' The type of this class is used to create the ResourceManager instance as the type name matches the name of the embedded resources file");
                _targetStream.WriteLine("    Friend Class SR");
                _targetStream.WriteLine("    ");
                _targetStream.WriteLine("    End Class");
                _targetStream.WriteLine("End Namespace");
            }
        }
        
        private enum TargetLanguage
        {
            CSharp, VB
        }

        internal static IEnumerable<KeyValuePair<string, string>> GetResources(string fileName)
        {
            XDocument doc = XDocument.Load(fileName, LoadOptions.PreserveWhitespace);
            foreach (XElement dataElem in doc.Element("root").Elements("data"))
            {
                string name = dataElem.Attribute("name").Value;
                string value = dataElem.Element("value").Value;
                yield return new KeyValuePair<string, string>(name, value);
            }
        }
    }
}
