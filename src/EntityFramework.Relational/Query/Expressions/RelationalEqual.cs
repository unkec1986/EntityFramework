// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq.Expressions;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Parsing;

namespace Microsoft.Data.Entity.Relational.Query.Expressions
{
    public abstract class RelationalBinaryExpression : ExtensionExpression
    {
        protected RelationalBinaryExpression(Expression left, Expression right, ExpressionType nodeType)
            : base(typeof(bool), nodeType)
        {
            Left = left;
            Right = right;
        }

        public Expression Left { get; private set; }
        public Expression Right { get; private set; }

        protected override Expression VisitChildren(ExpressionTreeVisitor visitor)
        {
            return this;
        }
    }

    public class RelationalEqual : RelationalBinaryExpression
    {
        public RelationalEqual(Expression left, Expression right)
            : base(left, right, ExpressionType.Equal)
        {
        }
    }

    public class RelationalNotEqual : RelationalBinaryExpression
    {
        public RelationalNotEqual(Expression left, Expression right)
            : base(left, right, ExpressionType.NotEqual)
        {
        }
    }
}
