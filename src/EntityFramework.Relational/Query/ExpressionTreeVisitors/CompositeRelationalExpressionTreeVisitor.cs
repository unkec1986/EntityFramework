// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq.Expressions;
using JetBrains.Annotations;
using Remotion.Linq.Parsing;

namespace Microsoft.Data.Entity.Relational.Query.ExpressionTreeVisitors
{
    public class CompositePredicateExpressionTreeVisitor : ExpressionTreeVisitor
    {
        private IDictionary<string, object> _parameterValues;

        public CompositePredicateExpressionTreeVisitor(IDictionary<string, object> parameterValues)
        {
            _parameterValues = parameterValues;
        }

        public override Expression VisitExpression(
            [NotNull]Expression expression)
        {
            var currentExpression = expression;
            var inExpressionOptimized = 
                new EqualityPredicateInExpressionOptimizer().VisitExpression(currentExpression);

            if (inExpressionOptimized != null)
            {
                currentExpression = inExpressionOptimized;
            }

            var negationOptimized =
                new PredicateNegationExpressionOptimizer(_parameterValues)
                .VisitExpression(currentExpression);

            if (negationOptimized != null)
            {
                currentExpression = negationOptimized;
            }

            var equalityExpanded =
                new EqualityPredicateExpandingVisitor(_parameterValues).VisitExpression(currentExpression);

            if (equalityExpanded != null)
            {
                currentExpression = equalityExpanded;
            }

            var nullSemanticsExpanded =
                new PredicateNullSemanticsExpandingVisitor(_parameterValues).VisitExpression(currentExpression);

            if (nullSemanticsExpanded != null)
            {
                currentExpression = nullSemanticsExpanded;
            }

            return currentExpression;
        }
    }
}
