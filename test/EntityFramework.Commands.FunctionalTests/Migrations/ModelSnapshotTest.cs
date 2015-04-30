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
        public class Sample
        {
            public int Id { get; set; }
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
        public void Entity_is_stored_in_model_snapshot()
        {
            var builder = new ModelBuilderFactory().CreateConventionBuilder();
            builder.Entity<Sample>();

            var code =
                 @"var builder = new ModelBuilder(new ConventionSet());

builder.Entity(""Microsoft.Data.Entity.Commands.Migrations.ModelSnapshotTest+Sample"", b =>
    {
        b.Property<int>(""Id"")
            .GenerateValueOnAdd()
            .Annotation(""OriginalValueIndex"", 0);
        b.Key(""Id"");
    });

return builder.Model;
";
            Test(builder.Model, code,
                o =>
                    {
                        Assert.Equal(1, o.EntityTypes.Count);
                        Assert.Equal("Microsoft.Data.Entity.Commands.Migrations.ModelSnapshotTest+Sample", o.EntityTypes.First().Name);
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
