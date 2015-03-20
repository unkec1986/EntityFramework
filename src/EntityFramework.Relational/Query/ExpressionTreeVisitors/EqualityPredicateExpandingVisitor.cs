// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq.Expressions;
using Microsoft.Data.Entity.Relational.Query.Expressions;
using JetBrains.Annotations;
using Remotion.Linq.Parsing;

namespace Microsoft.Data.Entity.Relational.Query.ExpressionTreeVisitors
{
    public class EqualityPredicateExpandingVisitor : ExpressionTreeVisitor
    {
        protected override Expression VisitBinaryExpression(
            [NotNull]BinaryExpression expression)
        {
            var left = VisitExpression(expression.Left);
            var right = VisitExpression(expression.Right);

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
                    {
                        return expression.NodeType == ExpressionType.Equal ?
                            Expression.Equal(
                                new CaseExpression(left),
                                new CaseExpression(right))
                            : Expression.NotEqual(
                                new CaseExpression(left),
                                new CaseExpression(right));

                        //return expression.NodeType == ExpressionType.Equal ?
                        //    Expression.OrElse(
                        //        Expression.AndAlso(
                        //            left,
                        //            right),
                        //        Expression.AndAlso(
                        //            Expression.Not(left),
                        //            Expression.Not(right)))
                        //    : Expression.OrElse(
                        //        Expression.AndAlso(
                        //            left,
                        //            Expression.Not(right)),
                        //        Expression.AndAlso(
                        //            Expression.Not(left),
                        //            right));
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
    }
}
