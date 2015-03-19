// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Microsoft.Data.Entity.Relational.Query.Expressions;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Parsing;

namespace Microsoft.Data.Entity.Relational.Query.ExpressionTreeVisitors
{
    public class PredicateNullSemanticsExpandingVisitor : ExpressionTreeVisitor
    {
        Dictionary<string, object> _parameterValues;

        public PredicateNullSemanticsExpandingVisitor(Dictionary<string, object> parameterValues)
        {
            _parameterValues = parameterValues;
        }
        protected override Expression VisitBinaryExpression(BinaryExpression expression)
        {
            if (expression.NodeType == ExpressionType.Equal || expression.NodeType == ExpressionType.NotEqual)
            {
                var left = VisitExpression(expression.Left);
                var right = VisitExpression(expression.Right);

                var leftNullables = ExtractNullableExpressions(expression.Left);
                var rightNullables = ExtractNullableExpressions(expression.Right);
                var leftNullable = leftNullables.Count > 0;
                var rightNullable = rightNullables.Count > 0;

                if (expression.NodeType == ExpressionType.Equal)
                {
                    if (leftNullable && rightNullable)
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

                        return Expression.AndAlso(
                            Expression.OrElse(
                                Expression.Equal(left, right),
                                Expression.AndAlso(
                                    new IsNullExpression(left),
                                    new IsNullExpression(right)
                                )
                            ),
                            Expression.OrElse(
                                Expression.AndAlso(
                                    new IsNotNullExpression(left),
                                    new IsNotNullExpression(right)
                                ),
                                Expression.AndAlso(
                                    new IsNullExpression(left),
                                    new IsNullExpression(right)
                                )
                            )
                        );
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
                        return Expression.AndAlso(
                            Expression.Equal(left, right),
                            new IsNotNullExpression(left));
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
                        var expanded = Expression.AndAlso(
                            Expression.Equal(left, right),
                            new IsNotNullExpression(left));
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

                        return Expression.OrElse(
                            Expression.AndAlso(
                                Expression.NotEqual(left, right),
                                Expression.OrElse(
                                    new IsNotNullExpression(left),
                                    new IsNotNullExpression(right)
                                )
                            ),
                            Expression.OrElse(
                                Expression.AndAlso(
                                    new IsNullExpression(left),
                                    new IsNotNullExpression(right)
                                ),
                                Expression.AndAlso(
                                    new IsNotNullExpression(left),
                                    new IsNullExpression(right)
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

                        return Expression.OrElse(
                            Expression.NotEqual(left, right),
                            new IsNullExpression(left));
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

                        return Expression.OrElse(
                            Expression.NotEqual(left, right),
                            new IsNullExpression(right));
                    }
                }
            }

            return base.VisitBinaryExpression(expression);
        }

        private List<Expression> ExtractNullableExpressions(Expression expression)
        {
            var nullableExpressionsExtractor = new NullableExpressionsExtractor(_parameterValues);
            _nullableExpressionsExtractor.VisitExpression(expression);

            return _nullableExpressionsExtractor.NullableExpressions;
        }

        private class NullableExpressionsExtractor : ExpressionTreeVisitor
        {
            Dictionary<string, object> _parameterValues;

            public NullableExpressionsExtractor(Dictionary<string, object> parameterValues)
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
                if (_parameterValues[expression.Name] == null)
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

                    return base.VisitExtensionExpression(expression);
                }

                var isNullExpression = expression as IsNullExpression;
                if (isNullExpression != null)
                {
                    return expression;
                }

                var isNotNullExpression = expression as IsNotNullExpression;
                if (isNotNullExpression != null)
                {
                    return expression;
                }

                return base.VisitExtensionExpression(expression);
            }
        }

        //private class ExpressionNullabilityChecker : ExpressionTreeVisitor
        //{
        //    Dictionary<string, object> _parameterValues;

        //    public ExpressionNullabilityChecker(Dictionary<string, object> parameterValues)
        //    {
        //        _parameterValues = parameterValues;
        //    }

        //    public bool IsNullable { get; set; }

        //    protected override Expression VisitConstantExpression(ConstantExpression expression)
        //    {
        //        IsNullable = expression.Value == null;

        //        return expression;
        //    }

        //    protected override Expression VisitExtensionExpression(ExtensionExpression expression)
        //    {
        //        var relationalBinary = expression as RelationalBinaryExpression;
        //        if (relationalBinary != null)
        //        {
        //            // we assume those are always optimized
        //            IsNullable = false;

        //            return expression;
        //        }

        //        var columnExpression = expression as ColumnExpression;
        //        if (columnExpression != null)
        //        {
        //            IsNullable = columnExpression.Property.IsNullable;

        //            return expression;
        //        }

        //        var isNullExpression = expression as IsNullExpression;
        //        if (isNullExpression != null)
        //        {
        //            IsNullable = false;

        //            return expression;
        //        }

        //        var isNotNullExpression = expression as IsNotNullExpression;
        //        if (isNotNullExpression != null)
        //        {
        //            IsNullable = false;

        //            return expression;
        //        }

        //        throw new InvalidOperationException("Unknown node!");

        //        //return base.VisitExtensionExpression(expression);
        //    }

        //    protected override Expression VisitParameterExpression(ParameterExpression expression)
        //    {
        //        if (_parameterValues[expression.Name] == null)
        //        {
        //            IsNullable = true;
        //        }

        //        return expression;
        //    }
        //}
    }
}
