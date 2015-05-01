﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Data.Entity.FunctionalTests;
using Microsoft.Data.Entity.FunctionalTests.TestModels.NullSemantics;

namespace Microsoft.Data.Entity.Relational.FunctionalTests
{
    public abstract class NullSemanticsQueryRelationalFixture<TTestStore> 
        where TTestStore : TestStore
    {
        public abstract TTestStore CreateTestStore();

        public abstract NullSemanticsContext CreateContext(TTestStore testStore);

        protected virtual void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<NullSemanticsEntity1>().Key(e => e.Id);
            modelBuilder.Entity<NullSemanticsEntity1>().Property(e => e.Id).GenerateValueOnAdd(false);

            modelBuilder.Entity<NullSemanticsEntity1>().Property(e => e.StringA).Required(true);
            modelBuilder.Entity<NullSemanticsEntity1>().Property(e => e.StringB).Required(true);
            modelBuilder.Entity<NullSemanticsEntity1>().Property(e => e.StringC).Required(true);

            modelBuilder.Entity<NullSemanticsEntity2>().Key(e => e.Id);
            modelBuilder.Entity<NullSemanticsEntity2>().Property(e => e.Id).GenerateValueOnAdd(false);

            modelBuilder.Entity<NullSemanticsEntity2>().Property(e => e.StringA).Required(true);
            modelBuilder.Entity<NullSemanticsEntity2>().Property(e => e.StringB).Required(true);
            modelBuilder.Entity<NullSemanticsEntity2>().Property(e => e.StringC).Required(true);
        }
    }
}
