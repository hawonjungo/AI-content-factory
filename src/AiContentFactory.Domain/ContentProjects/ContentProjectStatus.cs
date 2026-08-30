namespace AiContentFactory.Domain.ContentProjects;

public enum ContentProjectStatus
{
    Draft = 0,
    Researching = 1,
    ScriptReady = 2,
    StoryboardReady = 3,
    Generating = 4,
    Editing = 5,
    QA = 6,
    AwaitingApproval = 7,
    Approved = 8,
    Rejected = 9,
    Published = 10,
    Failed = 11
}
