// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq.Expressions;
using Microsoft.Data.Entity.Relational.Query.Expressions;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Parsing;

namespace Microsoft.Data.Entity.Relational.Query.ExpressionTreeVisitors
{
    public class PredicateNullSemanticsExpandingVisitor : ExpressionTreeVisitor
    {
        protected override Expression VisitBinaryExpression(BinaryExpression expression)
        {
            if (expression.NodeType == ExpressionType.Equal || expression.NodeType == ExpressionType.NotEqual)
            {
                var left = VisitExpression(expression.Left);
                var right = VisitExpression(expression.Right);

                var leftNullable = IsNullable(left);
                var rightNullable = IsNullable(right);

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


        private bool IsNullable(Expression expression)
        {
            var nullabilityChecker = new ExpressionNullabilityChecker();
            nullabilityChecker.VisitExpression(expression);

            return nullabilityChecker.IsNullable;
        }

        private class ExpressionNullabilityChecker : ExpressionTreeVisitor
        {
            public bool IsNullable { get; private set; }

            public ExpressionNullabilityChecker()
            {
                IsNullable = false;
            }

            protected override Expression VisitBinaryExpression(BinaryExpression expression)
            {
                // TODO: optimize for cases like true || some_nullable_expression

                VisitExpression(expression.Left);
                VisitExpression(expression.Right);

                return expression;
            }

            protected override Expression VisitUnaryExpression(UnaryExpression expression)
            {
                VisitExpression(expression.Operand);

                return expression;
            }

            protected override Expression VisitConstantExpression(ConstantExpression expression)
            {
                IsNullable = expression.Value == null;

                return expression;
            }

            protected override Expression VisitExtensionExpression(ExtensionExpression expression)
            {
                var relationalBinary = expression as RelationalBinaryExpression;
                if (relationalBinary != null)
                {
                    // we assume those are always optimized
                    IsNullable = false;

                    return expression;
                }

                var columnExpression = expression as ColumnExpression;
                if (columnExpression != null)
                {
                    IsNullable = columnExpression.Property.IsNullable;

                    return expression;
                }

                var isNullExpression = expression as IsNullExpression;
                if (isNullExpression != null)
                {
                    IsNullable = false;

                    return expression;
                }

                var isNotNullExpression = expression as IsNotNullExpression;
                if (isNotNullExpression != null)
                {
                    IsNullable = false;

                    return expression;
                }

                throw new InvalidOperationException("Unknown node!");

                //return base.VisitExtensionExpression(expression);
            }
        }

    }
}
