using MDD4All.Jira.DataModels;
using MDD4All.SpecIF.DataAccess.Jira;
using MDD4All.SpecIF.DataModels;
using MDD4All.SpecIF.DataModels.Helpers;
using MDD4All.SpecIF.DataProvider.Contracts;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MDD4All.SpecIF.DataModels.Manipulation;
using System.Diagnostics;
using Jira3 = MDD4All.Jira.DataModels.V3;
using MDD4All.SpecIF.DataFactory;
using MDD4All.Jira.DataAccess;

namespace MDD4All.SpecIF.DataProvider.Jira
{
    public class SpecIfJiraDataReader : AbstractSpecIfDataReader
    {
        private string _url = "";

        private JiraRestClient _jiraClient;

        private ISpecIfMetadataReader _metadataReader;

        private static List<Resource> ProjectRootResources = new List<Resource>();

        private static bool _projectInfoInitialized = false;

        private static Dictionary<string, Project> _projectInformations = new Dictionary<string, Project>();

        private static List<Jira3.Status> _statusInformations = new List<Jira3.Status>();

        public SpecIfJiraDataReader(string url,
                                    string userName,
                                    string apiKey, 
                                    ISpecIfMetadataReader metadataReader)
        {
            _url = url;
            _metadataReader = metadataReader;

            _jiraClient = new JiraRestClient(url, userName, apiKey);

            
            if (!_projectInfoInitialized)
            {
                InitializeProjectInformations();

                _projectInfoInitialized = true;
            }
        }

        public Project GetJiraProjectInfo(string specIfProjectID)
        {
            Project result = null;

            if(_projectInformations.ContainsKey(specIfProjectID))
            {
                result = _projectInformations[specIfProjectID];
            }
            else
            {
                InitializeProjectInformations();

                if(_projectInformations.ContainsKey(specIfProjectID))
                {
                    result = _projectInformations[specIfProjectID];
                }
            }

            return result;
        }

        private void InitializeProjectInformations()
        {
            Debug.Write("Initializing Jira project informations...");

            _projectInformations = new Dictionary<string, Project>();

            Task<ProjectSearchResponse> projectsTask = _jiraClient.GetJiraProjectsAsync();

            projectsTask.Wait();

            ProjectSearchResponse projectSearchResponse = projectsTask.Result;

            if(projectSearchResponse != null)
            {
                foreach(Project project in projectSearchResponse.Values)
                {
                    Task<Project> projectTask = _jiraClient.GetJiraProjectByKeyAsync(project.Key);

                    projectTask.Wait();

                    Project projectInfo = projectTask.Result;

                    if(projectInfo != null)
                    {
                        string specIfProjectID = JiraGuidConverter.ConvertToSpecIfGuid(projectInfo.Self, projectInfo.ID);

                        _projectInformations.Add(specIfProjectID, projectInfo);
                    }
                }
            }

            _statusInformations = new List<Jira3.Status>();

            Task<List<Jira3.Status>> statusTask = _jiraClient.GetStatusesAsync();

            statusTask.Wait();

            if(statusTask.Result != null)
            {
                _statusInformations = statusTask.Result;
            }
        }

        public override List<Node> GetAllHierarchies()
        {
            Task<List<Node>> task = CreateProjectHierarchiesAsync(false);

            task.Wait();

            return task.Result;
        }

        public override List<Node> GetAllHierarchyRootNodes(string projectID = null)
        {
            Task<List<Node>> task = CreateProjectHierarchiesAsync(true, projectID);

            task.Wait();

            return task.Result;
        }

        public override Node GetHierarchyByKey(Key key)
        {
            Node result = null;

            Task<List<Node>> task = CreateProjectHierarchiesAsync(false);

            task.Wait();

            List<Node> hierarchies = task.Result;

            foreach(Node hierarchy in hierarchies)
            {
                if(hierarchy.ID == key.ID /*&& hierarchy.Revision == key.Revision*/)
                {
                    result = hierarchy;
                    break;
                }
            }

            return result;
        }

        private async Task<List<Node>> CreateProjectHierarchiesAsync(bool rootNodesOnly, string projectFilter = null)
        {
            List<Node> result = new List<Node>();

            try
            {

                ProjectRootResources = new List<Resource>();

                ProjectSearchResponse projectSearchResponse = await _jiraClient.GetJiraProjectsAsync();



                foreach (Project project in projectSearchResponse.Values)
                {
                    string projectID = JiraGuidConverter.ConvertToSpecIfGuid(project.Self, project.ID);


                    if (projectFilter == null || (projectFilter != null && projectID == projectFilter))
                    {
                        string projectResourceID = "_" + SpecIfGuidGenerator.CalculateSha1Hash(project.Key);

                        Key resourceClass = new Key("RC-Hierarchy", "1.1");

                        Resource projectHierarchyResource = SpecIfDataFactory.CreateResource(resourceClass, _metadataReader);

                        projectHierarchyResource.ID = projectResourceID;
                        projectHierarchyResource.Revision = "1";

                        projectHierarchyResource.SetPropertyValue("dcterms:title", "Jira Project " + project.Key, _metadataReader);

                        projectHierarchyResource.SetPropertyValue("dcterms:description", project.Name, _metadataReader);


                        ProjectRootResources.Add(projectHierarchyResource);


                        Node projectNode = new Node
                        {
                            ID = projectResourceID + "_Node",
                            Revision = "1",
                            IsHierarchyRoot = true,
                            ProjectID = project.Key,
                            ResourceObject = new Key(projectHierarchyResource.ID, projectHierarchyResource.Revision)
                        };

                        result.Add(projectNode);

                        if (!rootNodesOnly)
                        {
                            IssueSearchResponse issueSearchResponse = await GetProjectIssuesAsync(project.Key);

                            if (issueSearchResponse != null)
                            {

                                foreach (Issue issue in issueSearchResponse.Issues)
                                {
                                    string issueResourceID = JiraGuidConverter.ConvertToSpecIfGuid(issue.Self, issue.ID);

                                    string issueRevision = SpecIfGuidGenerator.ConvertDateToRevision(issue.Fields.Updated.Value);

                                    Node requirementNode = new Node
                                    {
                                        ID = issueResourceID + "_Node",
                                        Revision = "1",
                                        IsHierarchyRoot = false,
                                        ProjectID = project.Key,
                                        ResourceObject = new Key(issueResourceID, issueRevision)
                                    };

                                    projectNode.Nodes.Add(requirementNode);
                                }
                            }
                        }
                    }
                }
            }
            catch(Exception exception)
            {
                Debug.WriteLine(exception);
            }

            return result;
        }


        private async Task<IssueSearchResponse> GetProjectIssuesAsync(string projectID)
        {
            IssueSearchResponse result = null;

            string jql = "";

            if (projectID != null)
            {
                jql = "project = \"" + projectID + "\" and type in (Requirement, \"Customer Requirement\")";
            }
            else
            {
                
            }

            result = await _jiraClient.GetIssuesByJQL(jql);

            return result;
        }

        public override List<Resource> GetAllResourceRevisions(string resourceID)
        {
            throw new NotImplementedException();
        }


        public override List<Statement> GetAllStatementRevisions(string statementID)
        {
            throw new NotImplementedException();
        }

        public override List<Statement> GetAllStatements()
        {
            throw new NotImplementedException();
        }

        public override List<Statement> GetAllStatementsForResource(Key resourceKey)
        {
            return new List<Statement>();
        }

        public override List<Node> GetChildNodes(Key parentNodeKey)
        {
            throw new NotImplementedException();
        }

        public override List<Node> GetContainingHierarchyRoots(Key resourceKey)
        {
            throw new NotImplementedException();
        }

        public override byte[] GetFile(string filename)
        {
            throw new NotImplementedException();
        }

        

        public override string GetLatestHierarchyRevision(string hierarchyID)
        {
            throw new NotImplementedException();
        }

        public override string GetLatestResourceRevisionForBranch(string resourceID, string branchName)
        {
            throw new NotImplementedException();
        }

        public override string GetLatestStatementRevision(string statementID)
        {
            throw new NotImplementedException();
        }

        public override Node GetNodeByKey(Key key)
        {
            throw new NotImplementedException();
        }

        public override Node GetParentNode(Key childNode)
        {
            throw new NotImplementedException();
        }

        public override DataModels.SpecIF GetProject(ISpecIfMetadataReader metadataReader, string projectID, List<Key> hierarchyFilter = null, bool includeMetadata = true)
        {
            throw new NotImplementedException();
        }

        public override List<ProjectDescriptor> GetProjectDescriptions()
        {
            List<ProjectDescriptor> result = new List<ProjectDescriptor>();

            Task<ProjectSearchResponse> task = _jiraClient.GetJiraProjectsAsync();

            task.Wait();

            ProjectSearchResponse projectSearchResponse = task.Result;

            if(projectSearchResponse != null)
            {
                foreach(Project jiraProject in projectSearchResponse.Values)
                {
                    string projectID = JiraGuidConverter.ConvertToSpecIfGuid(jiraProject.Self, jiraProject.ID);

                    ProjectDescriptor projectDescriptor = new ProjectDescriptor
                    {
                        ID = projectID,
                        Title = new List<MultilanguageText> {
                            new MultilanguageText(jiraProject.Name)
                        },
                        Generator = _url,
                        GeneratorVersion = "Jira REST API 2",
                        
                    };

                    result.Add(projectDescriptor);
                }
            }

            return result;
        }

        public override Resource GetResourceByKey(Key key)
        {
            Resource result = null;

            string issueKey = JiraGuidConverter.GetIssueIdFromSpecIfID(_url, key.ID);

            Jira3.Issue response;

            try
            {
                Task<Jira3.Issue> task = _jiraClient.GetJiraIssueAsync(issueKey);

                task.Wait();

                response = task.Result;

                //TODO: Get specific revision

                JiraToSpecIfConverter jiraToSpecIfConverter = new JiraToSpecIfConverter(_metadataReader, _statusInformations);

                result = jiraToSpecIfConverter.ConvertToResource(response);
            }
            catch(Exception exception)
            {
                Debug.WriteLine(exception);

                foreach(Resource resource in ProjectRootResources)
                {
                    if(resource.ID == key.ID && resource.Revision == key.Revision)
                    {
                        result = resource;
                        break;
                    }
                }
            }
             

            return result;
        }



        public override Statement GetStatementByKey(Key key)
        {
            throw new NotImplementedException();
        }
    }
}
