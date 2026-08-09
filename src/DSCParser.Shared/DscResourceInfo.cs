using System.Collections.Generic;
using System;
using System.Management.Automation;

namespace Microsoft.PowerShell.DesiredStateConfiguration
{
    /// <summary>
    /// Enumerated values for DSC resource implementation type
    /// </summary>
    public enum ImplementedAsType
    {
        /// <summary>
        /// DSC resource implementation type not known
        /// </summary>
        None = 0,

        /// <summary>
        /// DSC resource is implemented using PowerShell module
        /// </summary>
        PowerShell = 1,

        /// <summary>
        /// DSC resource is implemented using a CIM provider
        /// </summary>
        Binary = 2,

        /// <summary>
        /// DSC resource is a composite and implemented using configuration keyword
        /// </summary>
        Composite = 3
    }

    /// <summary>
    /// Contains a DSC resource information
    /// </summary>
    public sealed class DscResourceInfo
    {
        // Single source of truth for the properties. Both public shapes below read and write this
        // same list, so an append through either one is observable from the other.
        private readonly List<object> _properties = [];
        private readonly DscResourcePropertyInfoView _propertiesAsResourceInfo;

        /// <summary>
        /// Initializes a new instance of the DscResourceInfo class
        /// </summary>
        public DscResourceInfo()
        {
            _propertiesAsResourceInfo = new DscResourcePropertyInfoView(_properties);
        }

        /// <summary>
        /// Gets or sets resource type name
        /// </summary>
        public string? ResourceType { get; set; }

        /// <summary>
        /// Gets or sets Name of the resource. This name is used to access the resource
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets friendly name defined for the resource
        /// </summary>
        public string? FriendlyName { get; set; }

        /// <summary>
        /// Gets or sets module which implements the resource. This could point to parent module, if the DSC resource is implemented
        /// by one of nested modules.
        /// </summary>
        public PSModuleInfo? Module { get; set; }

        /// <summary>
        /// Gets name of the module which implements the resource.
        /// </summary>
        public string? ModuleName
        {
            get
            {
                return Module?.Name;
            }
        }

        /// <summary>
        /// Gets version of the module which implements the resource.
        /// </summary>
        public Version? Version
        {
            get
            {
                return Module?.Version;
            }
        }

        /// <summary>
        /// Gets or sets of the file which implements the resource. For the reosurces which are defined using
        /// MOF file, this will be path to a module which resides in the same folder where schema.mof file is present.
        /// For composite resources, this will be the module which implements the resource
        /// </summary>
        public string? Path { get; set; }

        /// <summary>
        /// Gets or sets parent folder, where the resource is defined
        /// It is the folder containing either the implementing module(=Path) or folder containing ".schema.mof".
        /// For native providers, Path will be null and only ParentPath will be present.
        /// </summary>
        public string? ParentPath { get; set; }

        /// <summary>
        /// Gets or sets a value which indicate how DSC resource is implemented
        /// </summary>
        public ImplementedAsType ImplementedAs { get; set; }

        /// <summary>
        /// Gets or sets company which owns this resource
        /// </summary>
        public string? CompanyName { get; set; }

        /// <summary>
        /// Gets the properties of the resource as a typed list. This is a live view over the same
        /// storage as <see cref="Properties"/>, not a copy, so mutating either is visible from both.
        /// </summary>
        public IList<DscResourcePropertyInfo> PropertiesAsResourceInfo => _propertiesAsResourceInfo;

        /// <summary>
        /// Gets the properties of the resource as a loosely typed list. This shape exists for
        /// Windows PowerShell interop, where the DSCResourcePropertyInfo type from
        /// Microsoft.Windows.DSC.CoreConfProviders.dll is incompatible with our own.
        /// </summary>
        public List<object> Properties => _properties;

        /// <summary>
        /// Adds a property to the resource.
        /// </summary>
        /// <param name="property">Property to add</param>
        public void AddProperty(DscResourcePropertyInfo property) => _properties.Add(property);

        /// <summary>
        /// Gets or sets implementation detail (e.g., "ScriptBased", "ClassBased")
        /// </summary>
        public string? ImplementationDetail { get; set; }

        /// <summary>
        /// Updates properties of the resource. Same as public variant, but accepts list of DscResourcePropertyInfo.
        /// Backwards compatibility for Windows PowerShell. It uses the DSCResourcePropertyInfo type from
        /// the Microsoft.Windows.DSC.CoreConfProviders.dll, which is incompatible with our own type.
        /// </summary>
        /// <param name="properties">Updated properties</param>
        public void UpdateProperties(List<DscResourcePropertyInfo> properties)
        {
            // Refill rather than reassign, so anything already holding Properties or
            // PropertiesAsResourceInfo keeps observing this resource.
            _properties.Clear();
            _properties.AddRange(properties);
        }

        /// <summary>
        /// Updates properties of the resource.
        /// </summary>
        /// <param name="properties">Updated properties</param>
        /// <exception cref="InvalidCastException">
        /// An element is not a <see cref="DscResourcePropertyInfo"/>.
        /// </exception>
        public void UpdateProperties(List<object> properties)
        {
            if (ReferenceEquals(properties, _properties))
            {
                return;
            }

            // Validate up front so a bad element leaves the existing properties untouched.
            foreach (object property in properties)
            {
                if (property is not null and not DscResourcePropertyInfo)
                {
                    throw new InvalidCastException(
                        $"Cannot store an object of type '{property.GetType().FullName}' as a DSC resource property.");
                }
            }

            _properties.Clear();
            _properties.AddRange(properties);
        }
    }

    /// <summary>
    /// Contains a DSC resource property information
    /// </summary>
    public sealed class DscResourcePropertyInfo
    {
        /// <summary>
        /// Initializes a new instance of the DscResourcePropertyInfo class
        /// </summary>
        public DscResourcePropertyInfo()
        {
        }

        /// <summary>
        /// Gets or sets name of the property
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets type of the property
        /// </summary>
        public string? PropertyType { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the property is mandatory or not
        /// </summary>
        public bool IsMandatory { get; set; }

        /// <summary>
        /// Gets Values for a resource property
        /// </summary>
        public List<string> Values { get; set; } = [];
    }
}
