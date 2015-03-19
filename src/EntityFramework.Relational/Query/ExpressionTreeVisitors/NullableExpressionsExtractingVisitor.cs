// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq.Expressions;
using Microsoft.Data.Entity.Relational.Query.Expressions;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Parsing;

namespace Microsoft.Data.Entity.Relational.Query.ExpressionTreeVisitors
{
    public class NullableExpressionsExtractingVisitor : ExpressionTreeVisitor
    {
        IDictionary<string, object> _parameterValues;

        public NullableExpressionsExtractingVisitor(IDictionary<string, object> parameterValues)
        {
            _parameterValues = parameterValues;
            NullableExpressions = new List<Expression>();
        }

        public List<Expression> NullableExpressions { get; private set; }

        protected override Expression VisitConstantExpression(ConstantExpression expression)
        {
            if (expression.Value == null)
            {
                NullableExpressions.Add(expression);
            }

            return base.VisitConstantExpression(expression);
        }

        protected override Expression VisitParameterExpression(ParameterExpression expression)
        {
            if (_parameterValues.Count == 0)
            {
                // if no parameter values are known assume all parameters are nullable
                NullableExpressions.Add(expression);
            }
            else if (_parameterValues[expression.Name] == null)
            {
                NullableExpressions.Add(expression);
            }

            return base.VisitParameterExpression(expression);
        }

        protected override Expression VisitExtensionExpression(ExtensionExpression expression)
        {
            var columnExpression = expression as ColumnExpression;
            if (columnExpression != null && columnExpression.Property.IsNullable)
            {
                NullableExpressions.Add(expression);

                return expression;
            }

            var isNullExpression = expression as IsNullExpression;
            if (isNullExpression != null)
            {
                return expression;
            }

            var inExpression = expression as InExpression;
            if (inExpression != null)
            {
                return expression;
            }

            return base.VisitExtensionExpression(expression);
        }
    }
}
