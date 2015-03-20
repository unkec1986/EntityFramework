// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Data.Entity.FunctionalTests;
using Microsoft.Data.Entity.Relational.FunctionalTests;
using Xunit;

namespace Microsoft.Data.Entity.SqlServer.FunctionalTests
{
    public class NullSemanticsQuerySqlServerTest : NullSemanticsQueryTestBase<SqlServerTestStore, NullSemanticsQuerySqlServerFixture>
    {
        public NullSemanticsQuerySqlServerTest(NullSemanticsQuerySqlServerFixture fixture)
            : base(fixture)
        {
        }

        public override void Compare_bool_with_bool_equal()
        {
            base.Compare_bool_with_bool_equal();

            Assert.Equal(
                @"SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE [e].[BoolA] = [e].[BoolB]

SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE (([e].[NullableBoolA] = [e].[NullableBoolB]) OR ([e].[NullableBoolA] IS NULL AND [e].[NullableBoolB] IS NULL)) AND (([e].[NullableBoolA] IS NOT NULL AND [e].[NullableBoolB] IS NOT NULL) OR ([e].[NullableBoolA] IS NULL AND [e].[NullableBoolB] IS NULL))",
                Sql);
        }

        public override void Compare_negated_bool_with_bool_equal()
        {
            base.Compare_negated_bool_with_bool_equal();

            Assert.Equal(
    @"SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE [e].[BoolA] <> [e].[BoolB]

SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE (([e].[NullableBoolA] <> [e].[NullableBoolB]) OR ([e].[NullableBoolA] IS NULL AND [e].[NullableBoolB] IS NULL)) AND ([e].[NullableBoolA] IS NULL OR [e].[NullableBoolB] IS NOT NULL) AND ([e].[NullableBoolA] IS NOT NULL OR [e].[NullableBoolB] IS NULL)",
    Sql);
        }

        public override void Compare_complex_not_equal_not_equal_not_equal()
        {
            base.Compare_complex_not_equal_not_equal_not_equal();

            Assert.Equal(
    @"SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE CASE WHEN (
    [e].[BoolA] <> [e].[BoolB]) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END <> CASE WHEN (
    [e].[IntA] <> [e].[IntB]) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END

SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE CASE WHEN (
    (([e].[NullableBoolA] <> [e].[NullableBoolB]) AND ([e].[NullableBoolA] IS NOT NULL OR [e].[NullableBoolB] IS NOT NULL)) OR ([e].[NullableBoolA] IS NULL AND [e].[NullableBoolB] IS NOT NULL) OR ([e].[NullableBoolA] IS NOT NULL AND [e].[NullableBoolB] IS NULL)) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END <> CASE WHEN (
    (([e].[NullableIntA] <> [e].[NullableIntB]) AND ([e].[NullableIntA] IS NOT NULL OR [e].[NullableIntB] IS NOT NULL)) OR ([e].[NullableIntA] IS NULL AND [e].[NullableIntB] IS NOT NULL) OR ([e].[NullableIntA] IS NOT NULL AND [e].[NullableIntB] IS NULL)) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END",
    Sql);
        }

        private static string Sql
        {
            get { return TestSqlLoggerFactory.Sql; }
        }
    }
}

