using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class ZoneHierarchyValidatorTests
{
    [Fact]
    public void Accepts_city_operating_zone_and_micro_zone_hierarchy()
    {
        Assert.Empty(ZoneHierarchyValidator.Validate(ValidHierarchy()));
    }

    [Fact]
    public void City_must_be_a_root()
    {
        var zones = ValidHierarchy().Append(new ZoneDefinition
        {
            ZoneId = "city-2",
            Kind = ZoneKind.City,
            Name = "Second City",
            ParentZoneId = "city-1"
        }).ToArray();

        var errors = ZoneHierarchyValidator.Validate(zones);

        Assert.Contains(errors, error => error.StartsWith("City zones", StringComparison.Ordinal));
    }

    [Fact]
    public void Operating_zone_requires_a_city_parent()
    {
        var zones = new[]
        {
            new ZoneDefinition { ZoneId = "zone-1", Kind = ZoneKind.OperatingZone, Name = "Central" }
        };

        var errors = ZoneHierarchyValidator.Validate(zones);

        Assert.Contains(errors, error => error.Contains("Operating zones require", StringComparison.Ordinal));
    }

    [Fact]
    public void Micro_zone_requires_an_operating_zone_parent()
    {
        var zones = new[]
        {
            new ZoneDefinition { ZoneId = "micro-1", Kind = ZoneKind.MicroZone, Name = "Block A" }
        };

        var errors = ZoneHierarchyValidator.Validate(zones);

        Assert.Contains(errors, error => error.Contains("Micro-zones require", StringComparison.Ordinal));
    }

    [Fact]
    public void Parent_must_exist_in_the_registry()
    {
        var zones = new[]
        {
            new ZoneDefinition
            {
                ZoneId = "zone-1",
                Kind = ZoneKind.OperatingZone,
                Name = "Central",
                ParentZoneId = "missing-city"
            }
        };

        var errors = ZoneHierarchyValidator.Validate(zones);

        Assert.Contains(errors, error => error.StartsWith("parent_zone_id", StringComparison.Ordinal));
    }

    [Fact]
    public void Parent_must_have_the_expected_kind()
    {
        var zones = new[]
        {
            new ZoneDefinition { ZoneId = "city-1", Kind = ZoneKind.City, Name = "Tehran" },
            new ZoneDefinition
            {
                ZoneId = "micro-1",
                Kind = ZoneKind.MicroZone,
                Name = "Block A",
                ParentZoneId = "city-1"
            }
        };

        var errors = ZoneHierarchyValidator.Validate(zones);

        Assert.Contains(errors, error => error.StartsWith("Micro-zone parent", StringComparison.Ordinal));
    }

    [Fact]
    public void Zone_ids_must_be_unique()
    {
        var zones = new[]
        {
            new ZoneDefinition { ZoneId = "city-1", Kind = ZoneKind.City, Name = "Tehran" },
            new ZoneDefinition { ZoneId = "city-1", Kind = ZoneKind.City, Name = "Duplicate" }
        };

        var errors = ZoneHierarchyValidator.Validate(zones);

        Assert.Contains(errors, error => error.StartsWith("zone_id values", StringComparison.Ordinal));
    }

    [Fact]
    public void Identifier_and_name_are_required()
    {
        var errors = ZoneHierarchyValidator.Validate(new[]
        {
            new ZoneDefinition { Kind = ZoneKind.City, Name = " " }
        });

        Assert.Contains(errors, error => error == "zone_id is required.");
        Assert.Contains(errors, error => error == "name is required.");
    }

    [Fact]
    public void Unknown_zone_kind_is_rejected()
    {
        var errors = ZoneHierarchyValidator.Validate(new[]
        {
            new ZoneDefinition { ZoneId = "unknown", Kind = (ZoneKind)99, Name = "Unknown" }
        });

        Assert.Contains(errors, error => error.StartsWith("kind is not", StringComparison.Ordinal));
    }

    [Fact]
    public void Opaque_metadata_is_trimmed_and_not_interpreted()
    {
        var zone = new ZoneDefinition
        {
            ZoneId = " city-1 ",
            Kind = ZoneKind.City,
            Name = " Tehran ",
            GeometryReference = " geometry:opaque ",
            Status = " future-state ",
            CapacityPolicyId = " policy:opaque "
        };

        Assert.Empty(ZoneHierarchyValidator.Validate(new[] { zone }));

        var normalized = zone.Normalize();
        Assert.Equal("city-1", normalized.ZoneId);
        Assert.Equal("Tehran", normalized.Name);
        Assert.Equal("geometry:opaque", normalized.GeometryReference);
        Assert.Equal("future-state", normalized.Status);
        Assert.Equal("policy:opaque", normalized.CapacityPolicyId);
    }

    [Fact]
    public void Empty_registry_is_valid()
    {
        Assert.Empty(ZoneHierarchyValidator.Validate(Array.Empty<ZoneDefinition>()));
    }

    [Fact]
    public void Null_registry_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ZoneHierarchyValidator.Validate(null!));
    }

    private static ZoneDefinition[] ValidHierarchy() =>
    [
        new ZoneDefinition { ZoneId = "city-1", Kind = ZoneKind.City, Name = "Tehran" },
        new ZoneDefinition
        {
            ZoneId = "zone-1",
            Kind = ZoneKind.OperatingZone,
            Name = "Central",
            ParentZoneId = "city-1",
            GeometryReference = "geometry:operating-1"
        },
        new ZoneDefinition
        {
            ZoneId = "micro-1",
            Kind = ZoneKind.MicroZone,
            Name = "Block A",
            ParentZoneId = "zone-1",
            GeometryReference = "geometry:micro-1"
        }
    ];
}
