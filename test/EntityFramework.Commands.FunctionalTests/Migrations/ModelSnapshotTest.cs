// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.Data.Entity.Commands.TestUtilities;
using Microsoft.Data.Entity.Commands.Utilities;
using Microsoft.Data.Entity.Metadata;
using Microsoft.Data.Entity.Metadata.Builders;
using Microsoft.Data.Entity.Utilities;
using Xunit;

namespace Microsoft.Data.Entity.Commands.Migrations
{
    public class ModelSnapshotTest
    {
        public class EntityWithOneProperty
        {
            public int Id { get; set; }
        }

        public class EntityWithTwoProperties
        {
            public int Id { get; set; }
            public int AlternateId { get; set; }
        }

        #region Model

        [Fact]
        public void Model_annotations_are_stored_in_snapshot()
        {
            var builder = new ModelBuilderFactory().CreateConventionBuilder();
            builder.Annotation("AnnotationName", "AnnotationValue");

            var code =
                 @"var builder = new ModelBuilder(new ConventionSet())
    .Annotation(""AnnotationName"", ""AnnotationValue"");

return builder.Model;
";
            Test(builder.Model, code,
                o =>
                {
                    Assert.Equal(1, o.Annotations.Count());
                    Assert.Equal("AnnotationName", o.Annotations.First().Name);
                    Assert.Equal("AnnotationValue", o.Annotations.First().Value);
                });
        }

        [Fact]
        public void Entities_are_stored_in_model_snapshot()
        {
            var builder = new ModelBuilderFactory().CreateConventionBuilder();
            builder.Entity<EntityWithOneProperty>();
            builder.Entity<EntityWithTwoProperties>();

            var code =
                 @"var builder = new ModelBuilder(new ConventionSet());

builder.Entity(""Microsoft.Data.Entity.Commands.Migrations.ModelSnapshotTest+EntityWithOneProperty"", b =>
    {
        b.Property<int>(""Id"")
            .GenerateValueOnAdd()
            .Annotation(""OriginalValueIndex"", 0);
        b.Key(""Id"");
    });

builder.Entity(""Microsoft.Data.Entity.Commands.Migrations.ModelSnapshotTest+EntityWithTwoProperties"", b =>
    {
        b.Property<int>(""Id"")
            .GenerateValueOnAdd()
            .Annotation(""OriginalValueIndex"", 0);
        b.Property<int>(""AlternateId"")
            .Annotation(""OriginalValueIndex"", 1);
        b.Key(""Id"");
    });

return builder.Model;
";
            Test(builder.Model, code,
                o =>
                    {
                        Assert.Equal(2, o.EntityTypes.Count);
                        Assert.Equal("Microsoft.Data.Entity.Commands.Migrations.ModelSnapshotTest+EntityWithOneProperty", o.EntityTypes[0].Name);
                        Assert.Equal("Microsoft.Data.Entity.Commands.Migrations.ModelSnapshotTest+EntityWithTwoProperties", o.EntityTypes[1].Name);
                    });
        }

        #endregion

        #region EntityType

        [Fact]
        public void EntityType_annotations_are_stored_in_snapshot()
        {
            var builder = new ModelBuilderFactory().CreateConventionBuilder();
            builder.Entity<EntityWithOneProperty>().Annotation("AnnotationName", "AnnotationValue");

            var code =
                 @"var builder = new ModelBuilder(new ConventionSet());

builder.Entity(""Microsoft.Data.Entity.Commands.Migrations.ModelSnapshotTest+EntityWithOneProperty"", b =>
    {
        b.Property<int>(""Id"")
            .GenerateValueOnAdd()
            .Annotation(""OriginalValueIndex"", 0);
        b.Key(""Id"");
        b.Annotation(""AnnotationName"", ""AnnotationValue"");
    });

return builder.Model;
";
            Test(builder.Model, code,
                o =>
                {
                    Assert.Equal(1, o.EntityTypes.First().Annotations.Count());
                    Assert.Equal("AnnotationName", o.EntityTypes.First().Annotations.First().Name);
                    Assert.Equal("AnnotationValue", o.EntityTypes.First().Annotations.First().Value);
                });
        }

        [Fact]
        public void Properties_are_stored_in_snapshot()
        {
            var builder = new ModelBuilderFactory().CreateConventionBuilder();
            builder.Entity<EntityWithTwoProperties>();

            var code =
                 @"var builder = new ModelBuilder(new ConventionSet());

builder.Entity(""Microsoft.Data.Entity.Commands.Migrations.ModelSnapshotTest+EntityWithTwoProperties"", b =>
    {
        b.Property<int>(""Id"")
            .GenerateValueOnAdd()
            .Annotation(""OriginalValueIndex"", 0);
        b.Property<int>(""AlternateId"")
            .Annotation(""OriginalValueIndex"", 1);
        b.Key(""Id"");
    });

return builder.Model;
";
            Test(builder.Model, code,
                o =>
                {
                    Assert.Equal(2, o.EntityTypes.First().GetProperties().Count());
                    Assert.Equal("Id", o.EntityTypes.First().GetProperties().ElementAt(0).Name);
                    Assert.Equal("AlternateId", o.EntityTypes.First().GetProperties().ElementAt(1).Name);
                });
        }

        [Fact]
        public void Primary_key_is_stored_in_snapshot()
        {
            var builder = new ModelBuilderFactory().CreateConventionBuilder();
            builder.Entity<EntityWithTwoProperties>().Key(t => new { t.Id, t.AlternateId });

            var code =
                 @"var builder = new ModelBuilder(new ConventionSet());

builder.Entity(""Microsoft.Data.Entity.Commands.Migrations.ModelSnapshotTest+EntityWithTwoProperties"", b =>
    {
        b.Property<int>(""Id"")
            .GenerateValueOnAdd()
            .Annotation(""OriginalValueIndex"", 0);
        b.Property<int>(""AlternateId"")
            .GenerateValueOnAdd()
            .Annotation(""OriginalValueIndex"", 1);
        b.Key(""Id"", ""AlternateId"");
    });

return builder.Model;
";
            Test(builder.Model, code,
                o =>
                {
                    Assert.Equal(2, o.EntityTypes.First().GetPrimaryKey().Properties.Count);
                    Assert.Equal("Id", o.EntityTypes.First().GetPrimaryKey().Properties[0].Name);
                    Assert.Equal("AlternateId", o.EntityTypes.First().GetPrimaryKey().Properties[1].Name);
                });
        }
        [Fact]
        public void Indexes_are_stored_in_snapshot()
        {
            var builder = new ModelBuilderFactory().CreateConventionBuilder();
            builder.Entity<EntityWithTwoProperties>().Index(t => t.AlternateId);

            var code =
                 @"var builder = new ModelBuilder(new ConventionSet());

builder.Entity(""Microsoft.Data.Entity.Commands.Migrations.ModelSnapshotTest+EntityWithTwoProperties"", b =>
    {
        b.Property<int>(""Id"")
            .GenerateValueOnAdd()
            .Annotation(""OriginalValueIndex"", 0);
        b.Property<int>(""AlternateId"")
            .Annotation(""OriginalValueIndex"", 1);
        b.Key(""Id"");
        b.Index(""AlternateId"");
    });

return builder.Model;
";
            Test(builder.Model, code,
                o =>
                {
                    Assert.Equal(1, o.EntityTypes.First().GetIndexes().Count());
                    Assert.Equal("AlternateId", o.EntityTypes.First().GetIndexes().First().Properties[0].Name);
                });
        }

        #endregion

        private void Test(IModel model, string expectedCode, Action<IModel> assert)
        {
            var generator = new CSharpModelGenerator(new CSharpHelper());

            var builder = new IndentedStringBuilder();
            generator.Generate(model, builder);
            var code = builder.ToString();

            Assert.Equal(expectedCode, code);

            var build = new BuildSource
            {
                References =
                {
                    BuildReference.ByName("System.Core, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"),
                    BuildReference.ByName("System.Linq.Expressions, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"),
                    BuildReference.ByName("System.Runtime, Version=4.0.10.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"),
                    BuildReference.ByName("EntityFramework.Core"),
                    BuildReference.ByName("EntityFramework.Relational"),
                },
                Source = @"
                    using System;
                    using Microsoft.Data.Entity;
                    using Microsoft.Data.Entity.Metadata;
                    using Microsoft.Data.Entity.Metadata.Builders;
                    using Microsoft.Data.Entity.Metadata.ModelConventions;
                    using Microsoft.Data.Entity.Relational.Migrations.Infrastructure;

                    
                    public static class ModelSnapshot
                    {
                        public static IModel Model
                        {
                            get
                            {
                                " + code + @"
                            }
                        }
                   }
                "
            };

            var assembly = build.BuildInMemory();
            var factoryType = assembly.GetType("ModelSnapshot");
            var property = factoryType.GetProperty("Model");
            var value = (IModel)property.GetValue(null);

            Assert.NotNull(value);
            assert(value);
        }
    }
}
