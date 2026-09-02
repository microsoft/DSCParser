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
        public static DscResourceInfo MapPSObjectToResourceInfo(object psObject)
        {
            if (psObject is null) throw new ArgumentNullException(nameof(psObject));

            if (Unwrap(psObject) is DscResourceInfo typed)
            {
                return typed;
            }

            PSObject source = PSObject.AsPSObject(psObject);

            DscResourceInfo resourceInfo = new()
            {
                ResourceType = AsString(Member(source, "ResourceType")),
                CompanyName = AsString(Member(source, "CompanyName")),
                FriendlyName = AsString(Member(source, "FriendlyName")),
                Module = Unwrap(Member(source, "Module")) as PSModuleInfo,
                Path = AsString(Member(source, "Path")),
                ParentPath = AsString(Member(source, "ParentPath")),
                ImplementedAs = (ImplementedAsType)Enum.Parse(typeof(ImplementedAsType), AsString(Member(source, "ImplementedAs"))),
                Name = AsString(Member(source, "Name"))
            };

            List<DscResourcePropertyInfo> props = [];
            foreach (object obj in AsEnumerable(Member(source, "Properties")))
            {
                props.Add(MapToDscResourcePropertyInfo(obj));
            }
            resourceInfo.UpdateProperties(props);

            return resourceInfo;
        }

        public static DscResourcePropertyInfo MapToDscResourcePropertyInfo(object psObjectPropery)
        {
            if (Unwrap(psObjectPropery) is DscResourcePropertyInfo typed)
            {
                return typed;
            }

            PSObject source = PSObject.AsPSObject(psObjectPropery);

            DscResourcePropertyInfo propertyInfo = new()
            {
                Name = AsString(Member(source, "Name")),
                PropertyType = AsString(Member(source, "PropertyType")),
                IsMandatory = LanguagePrimitives.IsTrue(Unwrap(Member(source, "IsMandatory")))
            };

            List<string> newValues = [];
            foreach (object value in AsEnumerable(Member(source, "Values")))
            {
                newValues.Add(AsString(value) ?? string.Empty);
            }
            propertyInfo.Values = newValues;
            return propertyInfo;
        }

        private static object? Member(PSObject source, string name)
        {
            return source.Properties[name]?.Value;
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
