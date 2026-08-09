using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using ImplementedAsType = Microsoft.PowerShell.DesiredStateConfiguration.ImplementedAsType;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;
using DscResourcePropertyInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourcePropertyInfo;

namespace DSCParser.CSharp
{
    internal sealed class DscResourceInfoMapper
    {
        public static DscResourceInfo MapPSObjectToResourceInfo(dynamic psObject)
        {
            if (psObject is null) throw new ArgumentNullException(nameof(psObject));

            DscResourceInfo resourceInfo = new()
            {
                ResourceType = AsString(psObject.ResourceType),
                CompanyName = AsString(psObject.CompanyName),
                FriendlyName = AsString(psObject.FriendlyName),
                Module = Unwrap(psObject.Module) as PSModuleInfo,
                Path = AsString(psObject.Path),
                ParentPath = AsString(psObject.ParentPath),
                ImplementedAs = (ImplementedAsType)Enum.Parse(typeof(ImplementedAsType), AsString(psObject.ImplementedAs)),
                Name = AsString(psObject.Name)
            };

            List<DscResourcePropertyInfo> props = [];
            foreach (object obj in AsEnumerable(psObject.Properties))
            {
                props.Add(MapToDscResourcePropertyInfo(obj));
            }
            resourceInfo.UpdateProperties(props);

            return resourceInfo;
        }

        public static DscResourcePropertyInfo MapToDscResourcePropertyInfo(dynamic psObjectPropery)
        {
            DscResourcePropertyInfo propertyInfo = new()
            {
                Name = AsString(psObjectPropery.Name),
                PropertyType = AsString(psObjectPropery.PropertyType),
                IsMandatory = LanguagePrimitives.IsTrue(Unwrap(psObjectPropery.IsMandatory))
            };

            List<string> newValues = [];
            foreach (object value in AsEnumerable(psObjectPropery.Values))
            {
                newValues.Add(AsString(value) ?? string.Empty);
            }
            propertyInfo.Values = newValues;
            return propertyInfo;
        }

        private static object? Unwrap(object? value)
        {
            return value is PSObject psObject ? psObject.BaseObject : value;
        }

        private static string? AsString(object? value)
        {
            return Unwrap(value)?.ToString();
        }

        private static IEnumerable AsEnumerable(object? value)
        {
            return Unwrap(value) as IEnumerable ?? Array.Empty<object>();
        }
    }
}
