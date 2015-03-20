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

        public CompositePredicateExpressionTreeVisitor(
            IDictionary<string, object> parameterValues)
        {
            _parameterValues = parameterValues;
        }

        public override Expression VisitExpression(
            [NotNull]Expression expression)
        {
            var currentExpression = expression;
            var inExpressionOptimized = 
                new EqualityPredicateInExpressionOptimizer().VisitExpression(currentExpression);

            currentExpression = inExpressionOptimized;

            var negationOptimized1 =
                new PredicateNegationExpressionOptimizer(_parameterValues)
                .VisitExpression(currentExpression);

            currentExpression = negationOptimized1;

            var equalityExpanded =
                new EqualityPredicateExpandingVisitor().VisitExpression(currentExpression);

            currentExpression = equalityExpanded;

            var negationOptimized2 =
                new PredicateNegationExpressionOptimizer(_parameterValues)
                .VisitExpression(currentExpression);

            currentExpression = negationOptimized2;

            if (_parameterValues.Count == 0)
            {
                var parameterFindingVisitor = new ParameterFindingVisitor();
                parameterFindingVisitor.VisitExpression(currentExpression);
                if (!parameterFindingVisitor.ContainsParametes)
                {
                    var nullSemanticsExpanded =
                        new PredicateNullSemanticsExpandingVisitor(_parameterValues)
                        .VisitExpression(currentExpression);

                    currentExpression = nullSemanticsExpanded;
                }
            }

            var negationOptimized3 =
                new PredicateNegationExpressionOptimizer(_parameterValues)
                .VisitExpression(currentExpression);

            currentExpression = negationOptimized3;

            return currentExpression;
        }

        private class ParameterFindingVisitor : ExpressionTreeVisitor
        {
            private bool _containsParameters = false;
            public bool ContainsParametes => _containsParameters;

            protected override Expression VisitParameterExpression(ParameterExpression expression)
            {
                _containsParameters = true;

                return expression;
            }
        }
    }
}
