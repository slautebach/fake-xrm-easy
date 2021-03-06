﻿using FakeItEasy;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk.Query;
using System.ServiceModel;
using Microsoft.Xrm.Sdk.Messages;
using FakeXrmEasy.Extensions;
using System.Reflection;

namespace FakeXrmEasy
{
    public partial class XrmFakedContext : IXrmContext
    {

        #region CRUD
        /// <summary>
        /// A fake retrieve method that will query the FakedContext to retrieve the specified
        /// entity and Guid, or null, if the entity was not found
        /// </summary>
        /// <param name="context">The faked context</param>
        /// <param name="fakedService">The faked service where the Retrieve method will be faked</param>
        /// <returns></returns>
        protected static void FakeRetrieve(XrmFakedContext context, IOrganizationService fakedService)
        {
            A.CallTo(() => fakedService.Retrieve(A<string>._, A<Guid>._, A<ColumnSet>._))
                .ReturnsLazily((string entityName, Guid id, ColumnSet columnSet) =>
                {
                    if (string.IsNullOrWhiteSpace(entityName))
                    {
                        throw new InvalidOperationException("The entity logical name must not be null or empty.");
                    }

                    if (id == Guid.Empty)
                    {
                        throw new InvalidOperationException("The id must not be empty.");
                    }

                    if (columnSet == null)
                    {
                        throw new InvalidOperationException("The columnset parameter must not be null.");
                    }

                    // Don't fail with invalid operation exception, if no record of this entity exists, but entity is known
                    if (!context.Data.ContainsKey(entityName))
                    {
                        if (context.ProxyTypesAssembly == null)
                        {
                            throw new InvalidOperationException(string.Format("The entity logical name {0} is not valid.",
                            entityName));
                        }

                        if (!context.ProxyTypesAssembly.GetTypes().Any(type => context.FindReflectedType(entityName) != null))
                        {
                            throw new InvalidOperationException(string.Format("The entity logical name {0} is not valid.",
                            entityName));
                        }
                    }

                    //Return the subset of columns requested only
                    var reflectedType = context.FindReflectedType(entityName);
                    
                    //Entity logical name exists, so , check if the requested entity exists
                    if (context.Data.ContainsKey(entityName) && context.Data[entityName] != null
                        && context.Data[entityName].ContainsKey(id))
                    {
                        //Entity found => return only the subset of columns specified or all of them
                        var foundEntity = context.Data[entityName][id].Clone(reflectedType);
                        if (columnSet.AllColumns) { 
                            foundEntity.ApplyDateBehaviour(context);
                            return foundEntity;
                        }
                        else
                        {
                            var projected = foundEntity.ProjectAttributes(columnSet, context);
                            projected.ApplyDateBehaviour(context);
                            return projected;
                        }
                    }
                    else
                    {
                        //Entity not found in the context => return null
                        throw new FaultException<OrganizationServiceFault>(new OrganizationServiceFault(), string.Format("{0} With Id = {1} Does Not Exist", entityName, id.ToString("D")));
                    }
                });
        }
        /// <summary>
        /// Fakes the Create message
        /// </summary>
        /// <param name="context"></param>
        /// <param name="fakedService"></param>
        protected static void FakeCreate(XrmFakedContext context, IOrganizationService fakedService)
        {
            A.CallTo(() => fakedService.Create(A<Entity>._))
                .ReturnsLazily((Entity e) =>
                {
                    context.CreateEntity(e);
           
                    return e.Id;
                });

        }

        protected static void FakeUpdate(XrmFakedContext context, IOrganizationService fakedService)
        {
            A.CallTo(() => fakedService.Update(A<Entity>._))
                .Invokes((Entity e) =>
                {
                    context.ValidateEntity(e);

                    //Update specific validations: The entity record must exist in the context
                    if (context.Data.ContainsKey(e.LogicalName) &&
                        context.Data[e.LogicalName].ContainsKey(e.Id))
                    {
                        //Add as many attributes to the entity as the ones received (this will keep existing ones)
                        var cachedEntity = context.Data[e.LogicalName][e.Id];
                        foreach (var sAttributeName in e.Attributes.Keys.ToList())
                        {
                            cachedEntity[sAttributeName] = e[sAttributeName];
                        }

                        // Update ModifiedOn
                        cachedEntity["modifiedon"] = DateTime.UtcNow;
                        cachedEntity["modifiedby"] = context.CallerId;
                    }
                    else
                    {
                        //The entity record was not found, return a CRM-ish update error message
                        throw new FaultException<OrganizationServiceFault>(new OrganizationServiceFault(), string.Format("{0} with Id {1} Does Not Exist", e.LogicalName, e.Id));
                    }
                });

        }

        /// <summary>
        /// Fakes the delete method. Very similar to the Retrieve one
        /// </summary>
        /// <param name="context"></param>
        /// <param name="fakedService"></param>
        protected static void FakeDelete(XrmFakedContext context, IOrganizationService fakedService)
        {
            A.CallTo(() => fakedService.Delete(A<string>._, A<Guid>._))
                .Invokes((string entityName, Guid id) =>
                {
                    if (string.IsNullOrWhiteSpace(entityName))
                    {
                        throw new InvalidOperationException("The entity logical name must not be null or empty.");
                    }

                    if (id == Guid.Empty)
                    {
                        throw new InvalidOperationException("The id must not be empty.");
                    }

                    // Don't fail with invalid operation exception, if no record of this entity exists, but entity is known
                    if (!context.Data.ContainsKey(entityName))
                    {
                        if (context.ProxyTypesAssembly == null)
                        {
                            throw new InvalidOperationException(string.Format("The entity logical name {0} is not valid.", entityName));
                        }

                        if (!context.ProxyTypesAssembly.GetTypes().Any(type => context.FindReflectedType(entityName) != null))
                        {
                            throw new InvalidOperationException(string.Format("The entity logical name {0} is not valid.", entityName));
                        }
                    }

                    //Entity logical name exists, so , check if the requested entity exists
                    if (context.Data.ContainsKey(entityName) && context.Data[entityName] != null && context.Data[entityName].ContainsKey(id))
                    {
                        //Entity found => return only the subset of columns specified or all of them
                        context.Data[entityName].Remove(id);
                    }
                    else
                    {
                        //Entity not found in the context => throw not found exception
                        //The entity record was not found, return a CRM-ish update error message
                        throw new FaultException<OrganizationServiceFault>(new OrganizationServiceFault(),
                            string.Format("{0} with Id {1} Does Not Exist", entityName, id));
                    }
                });
        }

        //protected static void FakeEnableProxyTypes(XrmFakedContext context, OrganizationServiceProxy fakedService)
        //{
        //    A.CallTo(() => fakedService.EnableProxyTypes(A<Assembly>._))
        //        .Invokes((Assembly a) =>
        //        {
        //            context.ProxyTypesAssembly = a;
        //        });

        //    A.CallTo(() => fakedService.EnableProxyTypes())
        //        .Invokes(() =>
        //        {
        //            //Do nothing, we implicitily allow proxy types

        //        });

        //}
        #endregion

        #region Other protected methods
        protected void EnsureEntityNameExistsInMetadata(string sEntityName)
        {
            if (Relationships.Values.Any(value => new[] { value.Entity1LogicalName, value.Entity2LogicalName, value.IntersectEntity }.Contains(sEntityName, StringComparer.InvariantCultureIgnoreCase)))
            {
                return;
            }
            //Entity metadata is checked differently when we are using a ProxyTypesAssembly => we can infer that from the generated types assembly
            if (ProxyTypesAssembly != null)
            {
                var subClassType = FindReflectedType(sEntityName);
                if (subClassType == null)
                {
                    throw new Exception(string.Format("Entity {0} does not exist in the metadata cache", sEntityName));
                }
            }
            //else if (!Data.ContainsKey(sEntityName))
            //{
            //    //No Proxy Types Assembly
            //    throw new Exception(string.Format("Entity {0} does not exist in the metadata cache", sEntityName));
            //};
        }

        protected void AddEntityDefaultAttributes(Entity e)
        {
            //Add createdon, modifiedon, createdby, modifiedby properties
            if (CallerId == null)
                CallerId = new EntityReference("systemuser", Guid.NewGuid()); //Create a new instance by default

            EntityInitializerService.Initialize(e, CallerId.Id);
        }

        protected void ValidateEntity(Entity e)
        {
            if (e == null)
            {
                throw new InvalidOperationException("The entity must not be null");
            }

            //Validate the entity
            if (string.IsNullOrWhiteSpace(e.LogicalName))
            {
                throw new InvalidOperationException("The LogicalName property must not be empty");
            }

            if (e.Id == Guid.Empty)
            {
                throw new InvalidOperationException("The Id property must not be empty");
            }
        }

        protected internal void CreateEntity(Entity e)
        {
            if (e == null)
            {
                throw new InvalidOperationException("The entity must not be null");
            }

            if (e.Id == Guid.Empty)
            {
                e.Id = Guid.NewGuid(); //Add default guid if none present
            }

            //Hack for Dynamic Entities where the Id property doesn't populate the "entitynameid" primary key
            var primaryKeyAttribute = string.Format("{0}id", e.LogicalName);
            if (!e.Attributes.ContainsKey(primaryKeyAttribute))
            {
                e[primaryKeyAttribute] = e.Id;
            }

            ValidateEntity(e);

            //Create specific validations
            if (e.Id != Guid.Empty && Data.ContainsKey(e.LogicalName) &&
                Data[e.LogicalName].ContainsKey(e.Id))
            {
                throw new InvalidOperationException(string.Format("There is already a record of entity {0} with id {1}, can't create with this Id.", e.LogicalName, e.Id));
            }

            AddEntityWithDefaults(e);

            if (e.RelatedEntities.Count > 0)
            {
                foreach (var relationshipSet in e.RelatedEntities)
                {
                    Relationship relationship = relationshipSet.Key;
                    foreach (var relatedEntity in relationshipSet.Value.Entities)
                    {
                        CreateEntity(relatedEntity);
                    }

                    if (FakeMessageExecutors.ContainsKey(typeof(AssociateRequest)))
                    {
                        var entityReferenceCollection = new EntityReferenceCollection(relationshipSet.Value.Entities.Select(en => en.ToEntityReference()).ToList());
                        var request = new AssociateRequest()
                        {
                            Target = new EntityReference() { Id = e.Id, LogicalName = e.LogicalName },
                            Relationship = relationship,
                            RelatedEntities = entityReferenceCollection
                        };
                        FakeMessageExecutors[typeof(AssociateRequest)].Execute(request, this);
                    }
                    else
                        throw PullRequestException.NotImplementedOrganizationRequest(typeof(AssociateRequest));
                }
            }
        }

        protected internal void AddEntityWithDefaults(Entity e)
        {
            var clone = e.Clone(e.GetType());
            AddEntityDefaultAttributes(clone);
            AddEntity(clone);
        }

        protected internal void AddEntity(Entity e)
        {
            //Automatically detect proxy types assembly if an early bound type was used.
            if (ProxyTypesAssembly == null &&
                e.GetType().IsSubclassOf(typeof(Entity)))
            {
                ProxyTypesAssembly = Assembly.GetAssembly(e.GetType());
            }

            ValidateEntity(e); //Entity must have a logical name and an Id

            //Add the entity collection
            if (!Data.ContainsKey(e.LogicalName))
            {
                Data.Add(e.LogicalName, new Dictionary<Guid, Entity>());
            }

            if (Data[e.LogicalName].ContainsKey(e.Id))
            {
                Data[e.LogicalName][e.Id] = e;
            }
            else
            {
                Data[e.LogicalName].Add(e.Id, e);
            }


            //Update metadata for that entity
            if (!AttributeMetadata.ContainsKey(e.LogicalName))
                AttributeMetadata.Add(e.LogicalName, new Dictionary<string, string>());

            //Update attribute metadata
            if (ProxyTypesAssembly != null)
            {
                //If the context is using a proxy types assembly then we can just guess the metadata from the generated attributes
                var type = FindReflectedType(e.LogicalName);
                if (type != null)
                {
                    var props = type.GetProperties();
                    foreach (var p in props)
                    {
                        if (!AttributeMetadata[e.LogicalName].ContainsKey(p.Name))
                            AttributeMetadata[e.LogicalName].Add(p.Name, p.Name);
                    }
                }
                else
                    throw new Exception(string.Format("Couldnt find reflected type for {0}", e.LogicalName));

            }
            else
            {
                //If dynamic entities are being used, then the only way of guessing if a property exists is just by checking
                //if the entity has the attribute in the dictionary
                foreach (var attKey in e.Attributes.Keys)
                {
                    if (!AttributeMetadata[e.LogicalName].ContainsKey(attKey))
                        AttributeMetadata[e.LogicalName].Add(attKey, attKey);
                }
            }

            
        }

        protected internal bool AttributeExistsInMetadata(string sEntityName, string sAttributeName)
        {
            var relationships = this.Relationships.Values.Where(value => new[] { value.Entity1LogicalName, value.Entity2LogicalName, value.IntersectEntity }.Contains(sEntityName, StringComparer.InvariantCultureIgnoreCase)).ToArray();
            if (relationships.Any(e => e.Entity1Attribute == sAttributeName || e.Entity2Attribute == sAttributeName))
            {
                return true;
            }

            //Early bound types
            if (ProxyTypesAssembly != null)
            {
                //Check if attribute exists in the early bound type 
                var earlyBoundType = FindReflectedType(sEntityName);
                if (earlyBoundType != null)
                {
                    //Get that type properties
                    var attributeFound = earlyBoundType
                        .GetProperties()
                        .Where(pi => pi.GetCustomAttributes(typeof(AttributeLogicalNameAttribute), true).Length > 0)
                        .Where(pi => (pi.GetCustomAttributes(typeof(AttributeLogicalNameAttribute), true)[0] as AttributeLogicalNameAttribute).LogicalName.Equals(sAttributeName))
                        .FirstOrDefault();

                    return attributeFound != null;
                }
                return false;
            }

            //Dynamic entities => just return true
            return true;
        }
        #endregion

    }
}