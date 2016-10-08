//
// Copyright (c) 2011 Francois Valdy
// Copyright (c) 2015 Timotei Dolean
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//

using System;
using System.Collections.Generic;
using Mono.Cecil;
using System.Linq;
using System.Text.RegularExpressions;
using Mono.Collections.Generic;

namespace ILRepacking.Steps
{
    internal class TypesRepackStep : IRepackStep
    {
        private readonly ILogger _logger;
        private readonly IRepackContext _repackContext;
        private readonly IRepackImporter _repackImporter;
        private readonly RepackOptions _repackOptions;

        public TypesRepackStep(
            ILogger logger,
            IRepackContext repackContext,
            IRepackImporter repackImporter,
            RepackOptions repackOptions)
        {
            _logger = logger;
            _repackContext = repackContext;
            _repackImporter = repackImporter;
            _repackOptions = repackOptions;
        }

        public void Perform()
        {
            HashSet<string> exposedTypeNames = new HashSet<string>();
            FindExposedTypes(exposedTypeNames);
            RepackTypes(exposedTypeNames);
            RepackExportedTypes(exposedTypeNames);
        }

        private IEnumerable<TypeReference> GetExposedGenericArguments(TypeReference reference, HashSet<string> whitelistedTypeNames)
        {
            if (reference.IsGenericInstance)
            {
                foreach (var argument in ((GenericInstanceType) reference).GenericArguments)
                {
                    if (!argument.IsGenericParameter)
                    {
                        if (whitelistedTypeNames.Contains(argument.FullName))
                            yield return argument;

                        foreach (var argument2 in GetExposedGenericArguments(argument, whitelistedTypeNames))
                            yield return argument2;
                    }
                }
            }
        }

        private IEnumerable<TypeReference> GetExposedTypes(TypeReference reference, HashSet<string> whitelistedTypeNames)
        {
            TypeDefinition definition = reference.Resolve();

            if (definition == null)
                yield break;

            if (definition.BaseType != null)
            {
                if (whitelistedTypeNames.Contains(definition.BaseType.FullName))
                {
                    yield return definition.BaseType;

                    foreach (var baseType in GetExposedTypes(definition.BaseType, whitelistedTypeNames))
                        yield return baseType;
                }

                if (definition.BaseType.IsGenericInstance)
                {
                    foreach (var argument in GetExposedGenericArguments((GenericInstanceType) definition.BaseType, whitelistedTypeNames))
                        yield return argument;
                }
            }

            foreach (var iface in definition.Interfaces)
            {
                if (whitelistedTypeNames.Contains(iface.FullName))
                    yield return iface;

                if (iface.IsGenericInstance)
                {
                    foreach (var argument in GetExposedGenericArguments((GenericInstanceType)iface, whitelistedTypeNames))
                        yield return argument;
                }
            }

            foreach (var method in definition.Methods.Where(x => x.IsPublic))
            {
                if (whitelistedTypeNames.Contains(method.ReturnType.FullName))
                    yield return method.ReturnType;

                if (method.ReturnType.IsGenericInstance)
                {
                    foreach (var argument in GetExposedGenericArguments((GenericInstanceType)method.ReturnType, whitelistedTypeNames))
                        yield return argument;
                }

                foreach (var param in method.Parameters)
                {
                    if (whitelistedTypeNames.Contains(param.ParameterType.FullName))
                        yield return param.ParameterType;

                    if (param.ParameterType.IsGenericInstance)
                    {
                        foreach (var argument in GetExposedGenericArguments((GenericInstanceType)param.ParameterType, whitelistedTypeNames))
                            yield return argument;
                    }
                }
            }

            foreach (var prop in definition.Properties)
            {
                if (prop.GetMethod != null && prop.GetMethod.IsPublic)
                {
                    if (whitelistedTypeNames.Contains(prop.GetMethod.ReturnType.FullName))
                        yield return prop.GetMethod.ReturnType;

                    if (prop.GetMethod.ReturnType.IsGenericInstance)
                    {
                        foreach (var argument in GetExposedGenericArguments((GenericInstanceType)prop.GetMethod.ReturnType, whitelistedTypeNames))
                            yield return argument;
                    }
                }
                else if (prop.SetMethod != null && prop.SetMethod.IsPublic)
                {
                    foreach (var param in prop.SetMethod.Parameters)
                    {
                        if (whitelistedTypeNames.Contains(param.ParameterType.FullName))
                            yield return param.ParameterType;

                        if (param.ParameterType.IsGenericInstance)
                        {
                            foreach (var argument in GetExposedGenericArguments((GenericInstanceType) param.ParameterType, whitelistedTypeNames))
                                yield return argument;
                        }
                    }
                }
            }

            foreach (var field in definition.Fields.Where(x => x.IsPublic))
            {
                if (whitelistedTypeNames.Contains(field.FieldType.FullName))
                    yield return field.FieldType;

                if (field.FieldType.IsGenericInstance)
                {
                    foreach (var argument in GetExposedGenericArguments((GenericInstanceType)field.FieldType, whitelistedTypeNames))
                        yield return argument;
                }
            }
        }

        private void FindExposedTypes(HashSet<string> exposedTypeNames)
        {
            List<TypeDefinition> typeDefinitions = new List<TypeDefinition>();

            foreach (var m in _repackContext.OtherAssemblies.SelectMany(x => x.Modules))
                typeDefinitions.AddRange(m.Types);

            HashSet<string> whitelistedTypeNames = new HashSet<string>(typeDefinitions.Select(x => x.FullName));

            foreach (var typeDefinition in typeDefinitions)
            {
                if (!ShouldInternalize(typeDefinition.FullName) && exposedTypeNames.Add(typeDefinition.FullName))
                    GetExposedTypes(exposedTypeNames, whitelistedTypeNames, typeDefinition);
            }

            foreach (var typeDefinition in _repackContext.PrimaryAssemblyDefinition.Modules.SelectMany(x => x.Types))
                GetExposedTypes(exposedTypeNames, whitelistedTypeNames, typeDefinition);
        }

        private void GetExposedTypes(HashSet<string> exposedTypeNames, HashSet<string> whitelistedTypeNames, TypeReference reference)
        {
            foreach (TypeReference exposedType in GetExposedTypes(reference, whitelistedTypeNames))
            {
                if (exposedTypeNames.Add(exposedType.FullName))
                    GetExposedTypes(exposedTypeNames, whitelistedTypeNames, exposedType);
            }
        }

        private void RepackTypes(HashSet<string> exposedTypeNames)
        {
            _logger.Info("Processing types");
            // merge types, this differs between 'primary' and 'other' assemblies regarding internalizing

            foreach (var r in _repackContext.PrimaryAssemblyDefinition.Modules.SelectMany(x => x.Types))
            {
                _logger.Verbose("- Importing " + r);
                _repackImporter.Import(r, _repackContext.TargetAssemblyMainModule.Types, false);
            }
            foreach (var m in _repackContext.OtherAssemblies.SelectMany(x => x.Modules))
            {
                foreach (var r in m.Types)
                {
                    _logger.Verbose("- Importing " + r);
                    _repackImporter.Import(r, _repackContext.TargetAssemblyMainModule.Types, !exposedTypeNames.Contains(r.FullName));
                }
            }
        }

        private void RepackExportedTypes(HashSet<string> exposedTypeNames)
        {
            var targetAssemblyMainModule = _repackContext.TargetAssemblyMainModule;
            _logger.Info("Processing types");
            foreach (var m in _repackContext.MergedAssemblies.SelectMany(x => x.Modules))
            {
                foreach (var r in m.ExportedTypes)
                {
                    _repackContext.MappingHandler.StoreExportedType(m, r.FullName, CreateReference(r));
                }
            }
            foreach (var r in _repackContext.PrimaryAssemblyDefinition.Modules.SelectMany(x => x.ExportedTypes))
            {
                _logger.Verbose("- Importing Exported Type" + r);
                _repackImporter.Import(r, targetAssemblyMainModule.ExportedTypes, targetAssemblyMainModule);
            }
            foreach (var m in _repackContext.OtherAssemblies.SelectMany(x => x.Modules))
            {
                foreach (var r in m.ExportedTypes)
                {
                    if (!exposedTypeNames.Contains(r.FullName))
                    {
                        _logger.Verbose("- Importing Exported Type " + r);
                        _repackImporter.Import(r, targetAssemblyMainModule.ExportedTypes, targetAssemblyMainModule);
                    }
                    else
                    {
                        _logger.Verbose("- Skipping Exported Type " + r);
                    }
                }
            }
        }

        /// <summary>
        /// Check if a type's FullName matches a Regex to exclude it from internalizing.
        /// </summary>
        private bool ShouldInternalize(string typeFullName)
        {
            if (_repackOptions.ExcludeInternalizeMatches == null)
            {
                return _repackOptions.Internalize;
            }
            string withSquareBrackets = "[" + typeFullName + "]";
            foreach (Regex r in _repackOptions.ExcludeInternalizeMatches)
                if (r.IsMatch(typeFullName) || r.IsMatch(withSquareBrackets))
                    return false;
            return true;
        }

        private TypeReference CreateReference(ExportedType type)
        {
            return new TypeReference(type.Namespace, type.Name, _repackContext.TargetAssemblyMainModule, _repackContext.MergeScope(type.Scope))
            {
                DeclaringType = type.DeclaringType != null ? CreateReference(type.DeclaringType) : null,
            };
        }
    }
}
