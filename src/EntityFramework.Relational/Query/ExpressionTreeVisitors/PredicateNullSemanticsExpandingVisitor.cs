// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq.Expressions;
using Microsoft.Data.Entity.Relational.Query.Expressions;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Parsing;

namespace Microsoft.Data.Entity.Relational.Query.ExpressionTreeVisitors
{
    public class PredicateNullSemanticsExpandingVisitor : ExpressionTreeVisitor
    {
        IDictionary<string, object> _parameterValues;

        //public Expression Process(Expression expression)
        //{
        //    var nullSemantics = VisitExpression(expression);
        //    var reduced = new ReducingExpressionVisitor().VisitExpression(nullSemantics);

        //    return reduced;
        //}

        public PredicateNullSemanticsExpandingVisitor(IDictionary<string, object> parameterValues)
        {
            _parameterValues = parameterValues;
        }

        protected override Expression VisitBinaryExpression(BinaryExpression expression)
        {
            var left = VisitExpression(expression.Left);
            var right = VisitExpression(expression.Right);
            if (expression.NodeType == ExpressionType.Equal || expression.NodeType == ExpressionType.NotEqual)
            {
                var leftNullables = ExtractNullableExpressions(left);
                var rightNullables = ExtractNullableExpressions(right);
                var leftNullable = leftNullables.Count > 0;
                var rightNullable = rightNullables.Count > 0;

                if (expression.NodeType == ExpressionType.Equal)
                {
                    var leftUnary = left as UnaryExpression;
                    if (leftUnary != null && leftUnary.NodeType == ExpressionType.Not)
                    {
                        return ExpandNegatedNullableEqualNullable(leftUnary.Operand, right, leftNullables, rightNullables);
                    }

                    if (leftNullable && rightNullable)
                    {
                        return ExpandNullableEqualNullable(left, right, leftNullables, rightNullables);
                    }

                    if (leftNullable && !rightNullable)
                    {
                        // (a == b) && (a != null)
                        //
                        // a | b | F1 = a == b | F2 = (a != null) | F3 = F1 && F2 | 
                        //   |   |             |                  |               |
                        // 0 | 0 | 1           | 1                | 1             |
                        // 0 | 1 | 0           | 1                | 0             |
                        // 1 | 0 | 0           | 1                | 0             |
                        // 1 | 1 | 1           | 1                | 1             |
                        // N | 0 | N           | 0                | 0             |
                        // N | 1 | N           | 0                | 0             |
                        return new NotNullableExpression(
                            Expression.AndAlso(
                                Expression.Equal(left, right),
                                BuildIsNotNullExpression(leftNullables)
                            )
                        );
                    }

                    if (!leftNullable && rightNullable)
                    {
                        // (a == b) && (b != null)
                        //
                        // a | b | F1 = a == b | F2 = (b != null) | F3 = F1 OR F2 | 
                        //   |   |             |                  |               |
                        // 0 | 0 | 1           | 1                | 1             |
                        // 0 | 1 | 0           | 1                | 0             |
                        // 0 | N | N           | 0                | 0             |
                        // 1 | 0 | 0           | 1                | 0             |
                        // 1 | 1 | 1           | 1                | 1             |
                        // 1 | N | N           | 0                | 0             |
                        return new NotNullableExpression(
                            Expression.AndAlso(
                                Expression.Equal(left, right),
                                BuildIsNotNullExpression(rightNullables)
                            )
                        );
                    }
                }

                if (expression.NodeType == ExpressionType.NotEqual)
                {
                    if (leftNullable && rightNullable)
                    {
                        // [(a != b) && (a != null || b != null)] || [(a == null && b != null) || (a != null && b == null)]
                        //
                        // a | b | F1 = a != b | F2 = (a == null && b == null) | F3 = F1 && F2 | 
                        //   |   |             |                               |               |
                        // 0 | 0 | 0           | 1                             | 0             |
                        // 0 | 1 | 1           | 1                             | 1             |
                        // 0 | N | N           | 1                             | N             |
                        // 1 | 0 | 1           | 1                             | 1             |
                        // 1 | 1 | 0           | 1                             | 0             |
                        // 1 | N | N           | 1                             | N             |
                        // N | 0 | N           | 1                             | N             |
                        // N | 1 | N           | 1                             | N             |
                        // N | N | N           | 0                             | 0             |
                        //
                        // a | b | f1 = (a == null && b != null) | f2 = (a != null && b == null) | F3 = F1 OR F2 | 
                        //   |   |                               |                               |               |
                        // 0 | 0 | 0                             | 0                             | 0             |
                        // 0 | 1 | 0                             | 0                             | 0             |
                        // 0 | N | 0                             | 1                             | 1             |
                        // 1 | 0 | 0                             | 0                             | 0             |
                        // 1 | 1 | 0                             | 0                             | 0             |
                        // 1 | N | 0                             | 1                             | 1             |
                        // N | 0 | 1                             | 0                             | 1             |
                        // N | 1 | 1                             | 0                             | 1             |
                        // N | N | 0                             | 0                             | 0             |
                        //
                        // a | b | Final = F3 OR f3 | 
                        //   |   |                  |
                        // 0 | 0 | 0 OR 0 = 0       |
                        // 0 | 1 | 1 OR 0 = 1       |
                        // 0 | N | N OR 1 = 1       |
                        // 1 | 0 | 1 OR 0 = 1       |
                        // 1 | 1 | 0 OR 0 = 0       |
                        // 1 | N | N OR 1 = 1       |
                        // N | 0 | N OR 1 = 1       |
                        // N | 1 | N OR 1 = 1       |
                        // N | N | 0 OR 0 = 0       |
                        return new NotNullableExpression(
                            Expression.OrElse(
                                Expression.AndAlso(
                                    Expression.NotEqual(left, right),
                                    Expression.OrElse(
                                        BuildIsNotNullExpression(leftNullables),
                                        BuildIsNotNullExpression(rightNullables)
                                    )
                                ),
                                Expression.OrElse(
                                    Expression.AndAlso(
                                        BuildIsNullExpression(leftNullables),
                                        BuildIsNotNullExpression(rightNullables)
                                    ),
                                    Expression.AndAlso(
                                        BuildIsNotNullExpression(leftNullables),
                                        BuildIsNullExpression(rightNullables)
                                    )
                                )
                            )
                        );
                    }

                    if (leftNullable && !rightNullable)
                    {
                        // (a != b) || (a == null)
                        //
                        // a | b | F1 = a != b | F2 = a == null | F3 = F1 OR F2 | 
                        //   |   |             |                |               |
                        // 0 | 0 | 0           | 0              | 0             |
                        // 0 | 1 | 1           | 0              | 1             |
                        // 1 | 0 | 1           | 0              | 1             |
                        // 1 | 1 | 0           | 0              | 0             |
                        // N | 0 | N           | 1              | 1             |
                        // N | 1 | N           | 1              | 1             |
                        return new NotNullableExpression(
                            Expression.OrElse(
                                Expression.NotEqual(left, right),
                                BuildIsNullExpression(leftNullables)
                            )
                        );
                    }

                    if (!leftNullable && rightNullable)
                    {
                        // [(a != b) || (b == null)
                        //
                        // a | b | F1 = a != b | F2 = b == null | F3 = F1 && F2 | 
                        //   |   |             |                |               |
                        // 0 | 0 | 0           | 0              | 0             |
                        // 0 | 1 | 1           | 0              | 1             |
                        // 0 | N | N           | 1              | 1             |
                        // 1 | 0 | 1           | 0              | 1             |
                        // 1 | 1 | 0           | 0              | 0             |
                        // 1 | N | N           | 1              | 1             |
                        return new NotNullableExpression(
                            Expression.OrElse(
                                Expression.NotEqual(left, right),
                                BuildIsNullExpression(rightNullables)
                            )
                        );
                    }
                }
            }

            if (left == expression.Left && right == expression.Right)
            {
                return expression;
            }
            else
            {
                return Expression.MakeBinary(expression.NodeType, left, right);
            }
        }

        protected override Expression VisitExtensionExpression(ExtensionExpression expression)
        {
            var notNullableExpression = expression as NotNullableExpression;
            if (notNullableExpression != null)
            {
                return expression;
            }

            return base.VisitExtensionExpression(expression);
        }

        private Expression ExpandNullableEqualNullable(
            Expression left, 
            Expression right, 
            List<Expression >leftNullables, 
            List<Expression> rightNullables)
        {
            // [(a == b) || (a == null && b == null)] && [(a != null && b != null) || (a is null && b is null)]
            //
            // a | b | F1 = a == b | F2 = (a == null && b == null) | F3 = F1 OR F2 | 
            //   |   |             |                               |               |
            // 0 | 0 | 1           | 0                             | 1             |
            // 0 | 1 | 0           | 0                             | 0             |
            // 0 | N | N           | 0                             | N             |
            // 1 | 0 | 0           | 0                             | 0             |
            // 1 | 1 | 1           | 0                             | 1             |
            // 1 | N | N           | 0                             | N             |
            // N | 0 | N           | 0                             | N             |
            // N | 1 | N           | 0                             | N             |
            // N | N | N           | 1                             | 1             |
            //
            // a | b | f1 = (a != null && b != null) | f2 = (a == null && b == null) | F3 = F1 OR F2 | 
            //   |   |                               |                               |               |
            // 0 | 0 | 1                             | 0                             | 1             |
            // 0 | 1 | 1                             | 0                             | 1             |
            // 0 | N | 0                             | 0                             | 0             |
            // 1 | 0 | 1                             | 0                             | 1             |
            // 1 | 1 | 1                             | 0                             | 1             |
            // 1 | N | 0                             | 0                             | 0             |
            // N | 0 | 0                             | 0                             | 0             |
            // N | 1 | 0                             | 0                             | 0             |
            // N | N | 0                             | 1                             | 1             |
            //
            // a | b | Final = F3 && f3 | 
            //   |   |                  |
            // 0 | 0 | 1 && 1 = 1       |
            // 0 | 1 | 0 && 1 = 0       |
            // 0 | N | N && 0 = 0       |
            // 1 | 0 | 0 && 1 = 0       |
            // 1 | 1 | 1 && 1 = 1       |
            // 1 | N | N && 0 = 0       |
            // N | 0 | N && 0 = 0       |
            // N | 1 | N && 0 = 0       |
            // N | N | 1 && 1 = 1       |
            return new NotNullableExpression(
                Expression.AndAlso(
                    Expression.OrElse(
                        Expression.Equal(left, right),
                        Expression.AndAlso(
                            BuildIsNullExpression(leftNullables),
                            BuildIsNullExpression(rightNullables)
                        )
                    ),
                    Expression.OrElse(
                        Expression.AndAlso(
                            BuildIsNotNullExpression(leftNullables),
                            BuildIsNotNullExpression(rightNullables)
                        ),
                        Expression.AndAlso(
                            BuildIsNullExpression(leftNullables),
                            BuildIsNullExpression(rightNullables)
                        )
                    )
                )
            );
        }

        private Expression ExpandNegatedNullableEqualNullable(
            Expression left,
            Expression right, 
            List<Expression> leftNullables,
            List<Expression> rightNullables)
        {
            return new NotNullableExpression(
                Expression.AndAlso(
                    Expression.OrElse(
                        Expression.NotEqual(left, right),
                        Expression.AndAlso(
                            BuildIsNullExpression(leftNullables),
                            BuildIsNullExpression(rightNullables)
                        )
                    ),
                    Expression.AndAlso(
                        Expression.OrElse(
                            BuildIsNullExpression(leftNullables),
                            BuildIsNotNullExpression(rightNullables)
                        ),
                        Expression.OrElse(
                            BuildIsNotNullExpression(leftNullables),
                            BuildIsNullExpression(rightNullables)
                        )
                    )
                )
            );
        }

        private Expression BuildIsNullExpression(List<Expression> nullableExpressions)
        {
            if (nullableExpressions.Count == 0)
            {
                return Expression.Constant(false);
            }

            if (nullableExpressions.Count == 1)
            {
                return new IsNullExpression(nullableExpressions[0]);
            }

            Expression current = nullableExpressions[0];
            for (int i = 1; i < nullableExpressions.Count; i++)
            {
                current = Expression.OrElse(current, nullableExpressions[i]);
            }

            return current;
        }

        private Expression BuildIsNotNullExpression(List<Expression> nullableExpressions)
        {
            if (nullableExpressions.Count == 0)
            {
                return Expression.Constant(true);
            }

            if (nullableExpressions.Count == 1)
            {
                return Expression.Not(new IsNullExpression(nullableExpressions[0]));
            }

            Expression current = nullableExpressions[0];
            for (int i = 1; i < nullableExpressions.Count; i++)
            {
                current = Expression.Not(Expression.AndAlso(current, nullableExpressions[i]));
            }

            return current;
        }

        private List<Expression> ExtractNullableExpressions(Expression expression)
        {
            var nullableExpressionsExtractor = new NullableExpressionsExtractingVisitor(_parameterValues);
            nullableExpressionsExtractor.VisitExpression(expression);

            return nullableExpressionsExtractor.NullableExpressions;
        }
    }
}
