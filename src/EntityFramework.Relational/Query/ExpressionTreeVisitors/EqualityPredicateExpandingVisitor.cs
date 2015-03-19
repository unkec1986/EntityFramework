// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq.Expressions;
using Microsoft.Data.Entity.Relational.Query.Expressions;
using JetBrains.Annotations;
using Remotion.Linq.Parsing;
using System.Collections.Generic;

namespace Microsoft.Data.Entity.Relational.Query.ExpressionTreeVisitors
{
    public class EqualityPredicateExpandingVisitor : ExpressionTreeVisitor
    {
        private IDictionary<string, object> _parameterValues;

        public EqualityPredicateExpandingVisitor(IDictionary<string, object> parameterValues)
        {
            _parameterValues = parameterValues;
        }

        protected override Expression VisitBinaryExpression(
            [NotNull]BinaryExpression expression)
        {
            if ((expression.NodeType == ExpressionType.Equal
                || expression.NodeType == ExpressionType.NotEqual)
                && expression.Left.Type == typeof(bool)
                && expression.Right.Type == typeof(bool))
            {
                var complexLeft = !(expression.Left is ColumnExpression
                    || expression.Left is ParameterExpression 
                    || expression.Left is ConstantExpression);

                var complexRight = !(expression.Right is ColumnExpression
                    || expression.Right is ParameterExpression
                    || expression.Right is ConstantExpression);

                if (complexLeft || complexRight)
                {
                    var leftNullable = ExtractNullableExpressions(expression.Left).Count > 0;
                    var rightNullable = ExtractNullableExpressions(expression.Right).Count > 0;

                    if (!leftNullable && !rightNullable)
                    {
                        var left = VisitExpression(expression.Left);
                        var right = VisitExpression(expression.Right);

                        return expression.NodeType == ExpressionType.Equal ?
                            Expression.OrElse(
                                Expression.AndAlso(
                                    left,
                                    right),
                                Expression.AndAlso(
                                    Expression.Not(expression.Left),
                                    Expression.Not(right)))
                            : Expression.OrElse(
                                Expression.AndAlso(
                                    left,
                                    Expression.Not(right)),
                                Expression.AndAlso(
                                    Expression.Not(left),
                                    right));
                    }
                }
            }

            return base.VisitBinaryExpression(expression);
        }

        private List<Expression> ExtractNullableExpressions(Expression expression)
        {
            var nullableExpressionsExtractor = new NullableExpressionsExtractingVisitor(
                _parameterValues);

            nullableExpressionsExtractor.VisitExpression(expression);

            return nullableExpressionsExtractor.NullableExpressions;
        }
    }
}
