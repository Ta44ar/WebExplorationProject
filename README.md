# Web Exploration Project

A universal project for analyzing web content credibility. Works with **any topic** - not limited to specific subjects.

## Table of Contents
- [Requirements](#requirements)
- [Installation](#installation)
- [Configuration](#configuration)
- [Usage](#usage)
- [Tasks](#tasks)
  - [Task 1: Web Crawling](#task-1-web-crawling)
  - [Task 2: Ranking](#task-2-ranking)
  - [Task 3: Clustering](#task-3-clustering)
  - [Task 4: Classification](#task-4-classification)
- [Universal Design](#universal-design)
- [Project Structure](#project-structure)
- [Results](#results)

---

## Requirements

- .NET 8.0 SDK
- Visual Studio 2022 or VS Code
- API Keys:
  - Google Custom Search API or Brave Search API (required)
  - OpenAI API (optional - for dictionary generation)

## Installation

```bash
git clone https://github.com/Ta44ar/WebExplorationProject.git
cd WebExplorationProject
dotnet restore
dotnet build
```

## Configuration

### API Keys (User Secrets)

```bash
# Google Search API
dotnet user-secrets set "GOOGLE_API_KEY" "your-key"
dotnet user-secrets set "GOOGLE_CX" "your-cx"

# Brave Search API
dotnet user-secrets set "BRAVE_API_KEY" "your-key"

# OpenAI (optional - for dictionary generation)
dotnet user-secrets set "OPENAI_API_KEY" "sk-..."
```

### appsettings.json

```json
{
  "Search": {
    "DefaultProvider": "Google",
    "Query": "do vaccines cause autism",
    "MaxResults": 20
  },
  "Crawl": {
    "Mode": "BFS",
    "MaxDepth": 3,
    "MaxWidth": 30
  },
  "Ranking": {
    "PositionWeight": 0.2,
    "ReferenceWeight": 0.25,
    "SpecialistWeight": 0.3,
    "EmotionWeight": 0.25,
    "GenerateDictionaries": true
  },
  "Clustering": {
    "ClusterCounts": "2,4,6",
    "MaxIterations": 100
  },
  "Classification": {
    "CrossValidationFolds": 5,
    "GroupCounts": "2,4"
  }
}
```

## Usage

```bash
# Interactive menu
dotnet run

# Direct task execution
dotnet run -- 1  # Task 1: Crawling
dotnet run -- 2  # Task 2: Ranking
dotnet run -- 3  # Task 3: Clustering
dotnet run -- 4  # Task 4: Classification
```

---

## Universal Design

The project is **fully universal** - works with any search query.

### Changing the Analysis Topic

1. **Edit `appsettings.json`:**
```json
{
  "Search": {
    "Query": "is coffee healthy"
  }
}
```

2. **Run Tasks 1-4** - the program will automatically:
   - Crawl pages for the new topic (Task 1)
   - Generate topic-specific dictionaries (Task 2)
   - Create rankings and clusters (Task 2-4)

### AI Dictionary Generation

If you have an OpenAI API key, the program generates topic-specific dictionaries:

```
Query: "is coffee healthy"

? OpenAI generates (1 request, ~$0.001):

SPECIALIST TERMS:
  caffeine, antioxidants, metabolism, blood pressure...

EMOTIONAL WORDS:
  miraculous, deadly, revolutionary, dangerous...

PROPAGANDA PHRASES:
  big coffee, hidden from you, scientists discovered...

? Saved to: data/generated_dictionaries.txt
```

### Without OpenAI Key

If no OpenAI key is available, the program uses default dictionaries (optimized for medical/vaccine topics).

---

## Tasks

### Task 1: Web Crawling

**Goal:** Retrieve web pages from search results.

**Algorithm:**
- Search query via Google/Brave API
- Crawl results using BFS or DFS
- Extract content, titles, metadata

**Configuration:**
| Parameter | Description | Default |
|-----------|-------------|---------|
| Mode | Traversal mode (BFS/DFS) | BFS |
| MaxDepth | Maximum crawl depth | 3 |
| MaxWidth | Maximum number of pages | 30 |

**Output:**
- `{Source}_BFS_graph.csv` - Crawled data
- `{Source}_BFS_graph.dot` - Graph in Graphviz format
- `{Source}_BFS_graph.png` - Graph visualization

---

### Task 2: Ranking

**Goal:** Evaluate page credibility based on 4 criteria.

**Formula:**
```
TotalScore = w1×PositionScore + w2×ReferenceScore + w3×SpecialistScore + w4×CredibilityScore
```

**Criteria:**

| Criterion | Weight | Description |
|-----------|--------|-------------|
| PositionScore | 0.20 | Search result position |
| ReferenceScore | 0.25 | External link diversity |
| SpecialistScore | 0.30 | Specialist terminology density |
| CredibilityScore | 0.25 | Absence of emotional/propaganda language |

**Output:**
- `{Source}_ranking.csv` - Ranking results
- `{Source}_ranking_details.txt` - Detailed report
- `Combined_ranking.csv` - Combined ranking
- `generated_dictionaries.txt` - AI dictionaries (if enabled)

---

### Task 3: Clustering (K-Means)

**Goal:** Group pages based on ranking scores.

**Algorithm:** K-Means (ML.NET)

**Configuration:**
| Parameter | Description | Default |
|-----------|-------------|---------|
| ClusterCounts | Number of clusters | 2, 4, 6 |
| MaxIterations | Maximum iterations | 100 |
| Seed | Random seed | 42 |

**Output:**
- `{Source}_clusters_k{K}.csv` - Cluster assignments
- `{Source}_clustering_report.txt` - Statistics report

---

### Task 4: Classification (Cross-Validation)

**Goal:** Classify pages with cross-validation.

**Algorithms:**

| Algorithm | Parameter Set 1 | Parameter Set 2 |
|-----------|-----------------|-----------------|
| FastTree | Leaves=10, MinData=5 | Leaves=20, MinData=10 |
| SdcaMaximumEntropy | L2=0.1 | L2=0.01 |

**Experiments:** 8 (2 algorithms × 2 parameters × 2 group sizes)

**Output:**
- `{Source}_classification_results.csv` - Experiment results
- `{Source}_classification_report.txt` - Detailed report

---

## Project Structure

```
WebExplorationProject/
??? Analysis/
?   ??? ClassificationService.cs    # Task 4: ML Classification
?   ??? ClusteringService.cs        # Task 3: K-Means clustering
?   ??? RankingService.cs           # Task 2: Page ranking
?   ??? DictionaryGeneratorService.cs # AI dictionary generation
??? Crawling/
?   ??? WebCrawlingService.cs       # Task 1: Web crawler
?   ??? BfsScheduler.cs             # BFS scheduler
?   ??? DfsScheduler.cs             # DFS scheduler
??? Models/
?   ??? ClassificationModels.cs     # Classification models
?   ??? ClusteringModels.cs         # Clustering models
?   ??? ClusteringConfiguration.cs  # Cluster configuration
?   ??? CrawledEdge.cs              # Graph edge model
?   ??? CrawlMode.cs                # BFS/DFS enum
?   ??? PageRanking.cs              # Ranking model
?   ??? RankingConfiguration.cs     # Ranking configuration
??? Search/
?   ??? ISearchProvider.cs          # Search interface
?   ??? GoogleSearchProvider.cs     # Google Custom Search
?   ??? BraveSearchProvider.cs      # Brave Search API
?   ??? SearchCacheService.cs       # Results cache
??? Tasks/
?   ??? ITask.cs                    # Task interface
?   ??? CrawlingTask.cs             # Task 1
?   ??? RankingTask.cs              # Task 2
?   ??? ClusteringTask.cs           # Task 3
?   ??? ClassificationTask.cs       # Task 4
??? docs/                           # Documentation
??? data/                           # Generated data
??? appsettings.json                # Configuration
??? Program.cs                      # Entry point
??? WebExplorationProject.csproj    # Project file
```

---

## Results

### Example Classification Results:

| Algorithm | Parameters | K=2 | K=4 |
|-----------|------------|-----|-----|
| FastTree | Leaves=10 | 99.34% | 94.64% |
| FastTree | Leaves=20 | 99.78% | 98.00% |
| SDCA | L2=0.01 | 97.72% | 72.57% |
| SDCA | L2=0.1 | 94.19% | 64.74% |

### Key Findings:
1. **FastTree** significantly outperforms SDCA
2. **K=2** is easier to classify than K=4
3. High accuracy (~99%) indicates good cluster separability

---

## Technologies

- **Language:** C# 12.0
- **Framework:** .NET 8.0
- **ML:** Microsoft.ML 5.0
- **Web Crawling:** Abot2
- **AI:** OpenAI API (dictionary generation only)
- **Logging:** Serilog

---

## Authors

Project created as part of a university course.

## License

MIT License
