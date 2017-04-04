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

using System.Collections.Generic;
using Mono.Cecil;
using System.Linq;
using System.Text.RegularExpressions;

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
                TypeReference type = definition.BaseType;

                if (type.IsGenericInstance)
                {
                    GenericInstanceType genericInstance = (GenericInstanceType)type;

                    if (whitelistedTypeNames.Contains(genericInstance.ElementType.FullName))
                        yield return genericInstance.ElementType;

                    foreach (var baseType in GetExposedTypes(type, whitelistedTypeNames))
                        yield return baseType;

                    foreach (var argument in GetExposedGenericArguments(genericInstance, whitelistedTypeNames))
                        yield return argument;
                }
                else
                {
                    if (whitelistedTypeNames.Contains(type.FullName))
                    {
                        yield return type;

                        foreach (var baseType in GetExposedTypes(type, whitelistedTypeNames))
                            yield return baseType;
                    }
                }
            }

            foreach (var iface in definition.Interfaces)
            {
                TypeReference type = iface;

                if (type.IsGenericInstance)
                {
                    GenericInstanceType genericInstance = (GenericInstanceType)type;

                    if (whitelistedTypeNames.Contains(genericInstance.ElementType.FullName))
                        yield return genericInstance.ElementType;

                    foreach (var argument in GetExposedGenericArguments(genericInstance, whitelistedTypeNames))
                        yield return argument;
                }
                else
                {
                    if (whitelistedTypeNames.Contains(type.FullName))
                        yield return type;
                }
            }

            foreach (var method in definition.Methods.Where(x => x.IsPublic))
            {
                TypeReference type = method.ReturnType;

                if (type.IsGenericInstance)
                {
                    GenericInstanceType genericInstance = (GenericInstanceType)type;

                    if (whitelistedTypeNames.Contains(genericInstance.ElementType.FullName))
                        yield return genericInstance.ElementType;

                    foreach (var argument in GetExposedGenericArguments(genericInstance, whitelistedTypeNames))
                        yield return argument;
                }
                else
                {
                    if (whitelistedTypeNames.Contains(type.FullName))
                        yield return type;
                }

                foreach (var param in method.Parameters)
                {
                    TypeReference paramType = param.ParameterType;

                    if (paramType.IsGenericInstance)
                    {
                        GenericInstanceType genericInstance = (GenericInstanceType)paramType;

                        if (whitelistedTypeNames.Contains(genericInstance.ElementType.FullName))
                            yield return genericInstance.ElementType;

                        foreach (var argument in GetExposedGenericArguments(genericInstance, whitelistedTypeNames))
                            yield return argument;
                    }
                    else
                    {
                        if (whitelistedTypeNames.Contains(paramType.FullName))
                            yield return paramType;
                    }
                }
            }

            foreach (var prop in definition.Properties)
            {
                if (prop.GetMethod != null && prop.GetMethod.IsPublic)
                {
                    TypeReference type = prop.GetMethod.ReturnType;

                    if (type.IsGenericInstance)
                    {
                        GenericInstanceType genericInstance = (GenericInstanceType)type;

                        if (whitelistedTypeNames.Contains(genericInstance.ElementType.FullName))
                            yield return genericInstance.ElementType;

                        foreach (var argument in GetExposedGenericArguments(genericInstance, whitelistedTypeNames))
                            yield return argument;
                    }
                    else
                    {
                        if (whitelistedTypeNames.Contains(type.FullName))
                            yield return type;
                    }
                }
                else if (prop.SetMethod != null && prop.SetMethod.IsPublic)
                {
                    foreach (var param in prop.SetMethod.Parameters)
                    {
                        TypeReference type = param.ParameterType;

                        if (type.IsGenericInstance)
                        {
                            GenericInstanceType genericInstance = (GenericInstanceType)type;

                            if (whitelistedTypeNames.Contains(genericInstance.ElementType.FullName))
                                yield return genericInstance.ElementType;

                            foreach (var argument in GetExposedGenericArguments(genericInstance, whitelistedTypeNames))
                                yield return argument;
                        }
                        else
                        {
                            if (whitelistedTypeNames.Contains(type.FullName))
                                yield return type;
                        }
                    }
                }
            }

            foreach (var field in definition.Fields.Where(x => x.IsPublic))
            {
                TypeReference type = field.FieldType;

                if (type.IsGenericInstance)
                {
                    GenericInstanceType genericInstance = (GenericInstanceType)type;

                    if (whitelistedTypeNames.Contains(genericInstance.ElementType.FullName))
                        yield return genericInstance.ElementType;

                    foreach (var argument in GetExposedGenericArguments(genericInstance, whitelistedTypeNames))
                        yield return argument;
                }
                else
                {
                    if (whitelistedTypeNames.Contains(type.FullName))
                        yield return type;
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
                _logger.Verbose($"- Importing {r} from {r.Module}");
                _repackImporter.Import(r, _repackContext.TargetAssemblyMainModule.Types, false);
            }

            foreach (var r in _repackContext.OtherAssemblies.SelectMany(x => x.Modules).SelectMany(m => m.Types))
            {
                _logger.Verbose($"- Importing {r} from {r.Module}");
                _repackImporter.Import(r, _repackContext.TargetAssemblyMainModule.Types, !exposedTypeNames.Contains(r.FullName));
            }
        }

        private void RepackExportedTypes(HashSet<string> exposedTypeNames)
        {
            var targetAssemblyMainModule = _repackContext.TargetAssemblyMainModule;
            _logger.Info("Processing exported types");
            foreach (var m in _repackContext.MergedAssemblies.SelectMany(x => x.Modules))
            {
                foreach (var r in m.ExportedTypes)
                {
                    _repackContext.MappingHandler.StoreExportedType(m, r.FullName, CreateReference(r));
                }
            }

            foreach (var r in _repackContext.PrimaryAssemblyDefinition.Modules.SelectMany(x => x.ExportedTypes))
            {
                _logger.Verbose($"- Importing Exported Type {r} from {r.Scope}");
                _repackImporter.Import(r, targetAssemblyMainModule.ExportedTypes, targetAssemblyMainModule);
            }

            foreach (var m in _repackContext.OtherAssemblies.SelectMany(x => x.Modules))
            {
                foreach (var r in m.ExportedTypes)
                {
                    if (!exposedTypeNames.Contains(r.FullName))
                    {
                        _logger.Verbose($"- Importing Exported Type {r} from {m}");
                        _repackImporter.Import(r, targetAssemblyMainModule.ExportedTypes, targetAssemblyMainModule);
                    }
                    else
                    {
                        _logger.Verbose($"- Skipping Exported Type {r} from {m}");
                    }
                }
            }
        }

        /// <summary>
        /// Check if a type's FullName matches a Regex to exclude it from internalizing.
        /// </summary>
        private bool ShouldInternalize(string typeFullName)
        {
            if (!_repackOptions.Internalize)
                return false;

            if (_repackOptions.ExcludeInternalizeMatches.Count == 0)
                return true;

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
