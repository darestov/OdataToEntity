﻿using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace OdataToEntity.Parsers.Translators
{
    internal sealed class OeSelectItemTranslator : SelectItemTranslator<OeSelectItem>
    {
        private readonly OeJoinBuilder _joinBuilder;
        private readonly OeSelectItem _navigationItem;
        private readonly bool _navigationNextLink;
        private readonly bool _skipToken;
        private Expression _source;
        private readonly OeQueryNodeVisitor _visitor;

        public OeSelectItemTranslator(OeSelectItem navigationItem, bool navigationNextLink,
            Expression source, OeJoinBuilder joinBuilder, bool skipToken)
        {
            _navigationItem = navigationItem;
            _navigationNextLink = navigationNextLink;
            _source = source;
            _joinBuilder = joinBuilder;
            _skipToken = skipToken;
            _visitor = joinBuilder.Visitor;
        }

        private Expression GetInnerSource(OeSelectItem navigationItem, ExpandedNavigationSelectItem item)
        {
            Type clrEntityType = _visitor.EdmModel.GetClrType(navigationItem.EdmProperty.DeclaringType);
            PropertyInfo navigationClrProperty = OeEdmClrHelper.GetPropertyIgnoreCase(clrEntityType, navigationItem.EdmProperty);

            Type itemType = OeExpressionHelper.GetCollectionItemType(navigationClrProperty.PropertyType);
            if (itemType == null)
                itemType = navigationClrProperty.PropertyType;

            var visitor = new OeQueryNodeVisitor(_visitor, Expression.Parameter(itemType));
            var expressionBuilder = new OeExpressionBuilder(_joinBuilder, visitor);

            Expression innerSource = Expression.Constant(null, typeof(IEnumerable<>).MakeGenericType(itemType));
            innerSource = expressionBuilder.ApplyFilter(innerSource, item.FilterOption);
            if (item.SkipOption != null || item.TopOption != null)
            {
                Expression source = Expression.Constant(null, typeof(IEnumerable<>).MakeGenericType(navigationClrProperty.DeclaringType));
                innerSource = OeCrossApplyBuilder.Build(source, innerSource, item, navigationItem.Path, expressionBuilder);
            }

            return innerSource;
        }
        public override OeSelectItem Translate(ExpandedNavigationSelectItem item)
        {
            if (_navigationNextLink && Cache.UriCompare.OeComparerExtension.GetNavigationNextLink(item))
                return null;

            Type itemType;
            IEdmNavigationProperty navigationProperty = (((NavigationPropertySegment)item.PathToNavigationProperty.LastSegment).NavigationProperty);
            OeSelectItem navigationItem = _navigationItem.FindChildrenNavigationItem(navigationProperty);
            if (navigationItem == null)
            {
                navigationItem = new OeSelectItem(_navigationItem, item, _skipToken);
                _navigationItem.AddNavigationItem(navigationItem);

                Expression innerSource = GetInnerSource(navigationItem, item);
                _source = _joinBuilder.Build(_source, innerSource, _navigationItem.GetJoinPath(), navigationProperty);

                itemType = OeExpressionHelper.GetCollectionItemType(innerSource.Type);
            }
            else
                itemType = _visitor.EdmModel.GetClrType(navigationItem.EntitySet.EntityType());

            var selectTranslator = new OeSelectTranslator(_joinBuilder, navigationItem);
            _source = selectTranslator.BuildSelect(item.SelectAndExpand, _source, _navigationNextLink, _skipToken);

            return navigationItem;
        }
        public override OeSelectItem Translate(PathSelectItem item)
        {
            if (item.SelectedPath.LastSegment is NavigationPropertySegment navigationSegment)
            {
                IEdmNavigationSource navigationSource = navigationSegment.NavigationSource;
                if (navigationSource == null)
                    navigationSource = OeEdmClrHelper.GetEntitySet(_visitor.EdmModel, navigationSegment.NavigationProperty.ToEntityType());
                var navigationSelectItem = new ExpandedNavigationSelectItem(new ODataExpandPath(item.SelectedPath), navigationSource, new SelectExpandClause(null, true));
                return Translate(navigationSelectItem);
            }

            if (item.SelectedPath.LastSegment is PropertySegment propertySegment)
                return new OeSelectItem(propertySegment.Property, _skipToken);

            throw new InvalidOperationException(item.SelectedPath.LastSegment.GetType().Name + " not supported");
        }

        public Expression Source => _source;
    }
}
